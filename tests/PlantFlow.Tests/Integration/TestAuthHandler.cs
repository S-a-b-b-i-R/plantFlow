using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PlantFlow.Tests.Integration;

// Stands in for Firebase token validation. The header carries "uid|email" and we
// turn it into the same claims a verified Firebase token would have.
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName  = "Test";
    public const string Header = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parts = value.ToString().Split('|');
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", parts[0]),
                new Claim("email", parts[1]),
                new Claim("email_verified", "true"),
                new Claim("name", parts[1])
            ],
            SchemeName );

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName );
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}