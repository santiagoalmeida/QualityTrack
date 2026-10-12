using QualityTrack.Application.Accounts;

namespace QualityTrack.Web.Auth;

/// <summary>Until the first organization exists, every page leads to the setup screen.</summary>
public class SetupGate(RequestDelegate next)
{
    // Setup happens once and is never undone, so a positive answer can be remembered for the process lifetime.
    private volatile bool _configured;

    public async Task InvokeAsync(HttpContext http, AccountService accounts)
    {
        var path = http.Request.Path;
        var exempt = path.StartsWithSegments("/setup")
            || path.StartsWithSegments("/api")
            || path.StartsWithSegments("/_framework")
            || Path.HasExtension(path.Value);

        if (!_configured && !exempt)
        {
            if (await accounts.IsSetupRequiredAsync(http.RequestAborted))
            {
                http.Response.Redirect("/setup");
                return;
            }

            _configured = true;
        }

        await next(http);
    }
}
