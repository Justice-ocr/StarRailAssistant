using System.Text;
using System.Text.Json;

namespace SRAFrontend.Server.Services;

public sealed class SraApiClient(HttpClient httpClient, IConfiguration configuration)
{
    private HttpClient Client => httpClient;

    private string BaseUrl => $"http://127.0.0.1:{configuration.GetValue("SraServer:Port", 5073)}";

    private string ApiKey => configuration.GetValue("SraServer:ApiKey", string.Empty);

    private void PrepareRequest(HttpRequestMessage request)
    {
        request.Headers.Remove("X-Api-Key");
        if (!string.IsNullOrWhiteSpace(ApiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", ApiKey);
    }

    private async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> GetTaskStatusAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/Task/status");
        PrepareRequest(request);
        var response = await Client.SendAsync(request, ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonElement> GetSettingsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/Settings");
        PrepareRequest(request);
        var response = await Client.SendAsync(request, ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonElement> UpdateSettingsAsync(JsonElement body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/Settings")
        {
            Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")
        };
        PrepareRequest(request);
        var response = await Client.SendAsync(request, ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonElement> ListConfigsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/Configs");
        PrepareRequest(request);
        var response = await Client.SendAsync(request, ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonElement> CreateConfigAsync(string name, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/Configs/{Uri.EscapeDataString(name)}");
        PrepareRequest(request);
        var response = await Client.SendAsync(request, ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonElement?> GetConfigAsync(string name, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/Configs/{Uri.EscapeDataString(name)}");
        PrepareRequest(request);
        var response = await Client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonElement> UpdateConfigAsync(string name, JsonElement body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/Configs/{Uri.EscapeDataString(name)}")
        {
            Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")
        };
        PrepareRequest(request);
        var response = await Client.SendAsync(request, ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonElement> StartTaskAsync(string? configName, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/Task/run")
        {
            Content = JsonContent.Create(new { configName })
        };
        PrepareRequest(request);
        var response = await Client.SendAsync(request, ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonElement> StopTaskAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/Task/stop");
        PrepareRequest(request);
        var response = await Client.SendAsync(request, ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<JsonElement> GetLogsAsync(int count, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/Task/logs?count={count}");
        PrepareRequest(request);
        var response = await Client.SendAsync(request, ct);
        return await ReadJsonAsync(response, ct);
    }

    public async Task<HttpResponseMessage> OpenLogStreamAsync(CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/Task/logs/stream");
        PrepareRequest(request);
        return await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
}
