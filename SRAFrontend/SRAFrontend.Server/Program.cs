using Microsoft.AspNetCore.Mvc.Authorization;
using System.Text.Json;
using SRAFrontend.Server.Services;
using SRAFrontend.Server.Utils;
using SRAFrontend.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<PyBackendService>();
builder.Services.AddSingleton<CliBackendService>();
builder.Services.AddSingleton<IBackendService, BackendServiceProxy>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<CacheService>();
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<LogStreamService>();
builder.Services.AddHostedService<HostedService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<SraServerSupervisor>();
builder.Services.AddSingleton<SraApiClient>();
builder.Services.AddSingleton<SraLogStreamService>();

// API Key 认证
var apiKey = builder.Configuration["ApiKey"];
var authEnabled = !string.IsNullOrWhiteSpace(apiKey);
builder.Services.AddAuthentication(ApiKeyAuthenticationOptions.DefaultScheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationOptions.DefaultScheme, _ => { });
if (authEnabled)
    builder.Services.AddAuthorization();

builder.Services.AddControllers(options =>
{
    if (authEnabled)
        options.Filters.Add(new AuthorizeFilter());
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

if (!authEnabled)
    app.Logger.LogWarning("ApiKey is not set; server is unsecured.");

// Configure the HTTP request pipeline.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapOpenApi();

// app.UseHttpsRedirection();

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapControllers();
app.MapGet("/api/health", async (SraServerSupervisor supervisor, CancellationToken ct) =>
{
    var state = await supervisor.GetStateAsync(ct);
    return Results.Ok(new { ok = true, service = "SRAWebHost", sra = state });
});

app.MapGet("/api/status", async (SraServerSupervisor supervisor, SraApiClient client, CancellationToken ct) =>
{
    await supervisor.EnsureStartedAsync(ct);
    return Results.Ok(await client.GetTaskStatusAsync(ct));
});

app.MapGet("/api/settings", async (SraServerSupervisor supervisor, SraApiClient client, CancellationToken ct) =>
{
    await supervisor.EnsureStartedAsync(ct);
    return Results.Ok(await client.GetSettingsAsync(ct));
});

app.MapPut("/api/settings", async (SraServerSupervisor supervisor, SraApiClient client, HttpContext ctx, CancellationToken ct) =>
{
    await supervisor.EnsureStartedAsync(ct);
    var payload = await ctx.Request.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    if (payload.ValueKind == JsonValueKind.Undefined)
        return Results.BadRequest(new { message = "Invalid JSON" });
    return Results.Ok(await client.UpdateSettingsAsync(payload, ct));
});

app.MapGet("/api/configs", async (SraServerSupervisor supervisor, SraApiClient client, CancellationToken ct) =>
{
    await supervisor.EnsureStartedAsync(ct);
    return Results.Ok(await client.ListConfigsAsync(ct));
});

app.MapPost("/api/configs/{name}", async (string name, SraServerSupervisor supervisor, SraApiClient client, CancellationToken ct) =>
{
    await supervisor.EnsureStartedAsync(ct);
    return Results.Ok(await client.CreateConfigAsync(name, ct));
});

app.MapGet("/api/configs/{name}", async (string name, SraServerSupervisor supervisor, SraApiClient client, CancellationToken ct) =>
{
    await supervisor.EnsureStartedAsync(ct);
    var config = await client.GetConfigAsync(name, ct);
    return config is null ? Results.NotFound() : Results.Ok(config);
});

app.MapPut("/api/configs/{name}", async (string name, SraServerSupervisor supervisor, SraApiClient client, HttpContext ctx, CancellationToken ct) =>
{
    await supervisor.EnsureStartedAsync(ct);
    var payload = await ctx.Request.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    if (payload.ValueKind == JsonValueKind.Undefined)
        return Results.BadRequest(new { message = "Invalid JSON" });
    return Results.Ok(await client.UpdateConfigAsync(name, payload, ct));
});

app.MapPost("/api/task/start", async (SraServerSupervisor supervisor, SraApiClient client, HttpContext ctx, CancellationToken ct) =>
{
    await supervisor.EnsureStartedAsync(ct);
    var body = await ctx.Request.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    string? configName = null;
    if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("configName", out var configProp))
        configName = configProp.GetString();
    return Results.Ok(await client.StartTaskAsync(configName, ct));
});

app.MapPost("/api/task/stop", async (SraServerSupervisor supervisor, SraApiClient client, CancellationToken ct) =>
{
    await supervisor.EnsureStartedAsync(ct);
    return Results.Ok(await client.StopTaskAsync(ct));
});

app.MapGet("/api/logs", async (int count, SraServerSupervisor supervisor, SraApiClient client, CancellationToken ct) =>
{
    await supervisor.EnsureStartedAsync(ct);
    return Results.Ok(await client.GetLogsAsync(count, ct));
});

app.MapGet("/api/logs/stream", async (SraServerSupervisor supervisor, SraApiClient client, HttpContext context, CancellationToken ct) =>
{
    await supervisor.EnsureStartedAsync(ct);
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    using var response = await client.OpenLogStreamAsync(ct);
    await using var stream = await response.Content.ReadAsStreamAsync(ct);
    using var reader = new StreamReader(stream);

    while (!ct.IsCancellationRequested)
    {
        var line = await reader.ReadLineAsync();
        if (line is null)
            break;

        await context.Response.WriteAsync($"data: {line}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }
});
app.MapFallbackToFile("index.html");

app.Run();
