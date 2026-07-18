using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SRAFrontend.Server.Utils;

public class TokenAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "AccessToken";
    public const string HeaderName = "X-Access-Token";
}

public class TokenAuthenticationHandler(
    IOptionsMonitor<TokenAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<TokenAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredToken = configuration[TokenAuthenticationOptions.DefaultScheme];
        var providedToken = GetProvidedToken();

        if (string.IsNullOrWhiteSpace(configuredToken) ||
            string.IsNullOrWhiteSpace(providedToken) ||
            !string.Equals(providedToken, configuredToken, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Access token"));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "User") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string? GetProvidedToken()
    {
        if (Request.Headers.TryGetValue(TokenAuthenticationOptions.HeaderName, out var headerValues))
            return headerValues.FirstOrDefault();

        return null;
    }
}
