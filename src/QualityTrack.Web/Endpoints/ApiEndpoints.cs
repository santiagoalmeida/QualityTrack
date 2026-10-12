using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using QualityTrack.Application.Accounts;
using QualityTrack.Application.ApiKeys;
using QualityTrack.Application.Common;
using QualityTrack.Application.Ingestion;

namespace QualityTrack.Web.Endpoints;

public static class ApiEndpoints
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string CliLoginPath = "/api/v1/cli/login";

    public static void MapQualityTrackEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

        app.MapPost("/api/v1/runs", IngestRunAsync).AllowAnonymous().DisableAntiforgery();

        app.MapPost(CliLoginPath, CliLoginAsync).AllowAnonymous().DisableAntiforgery();

        // Lets a script know that its login will create the organization, so it can ask for the password twice.
        app.MapGet("/api/v1/cli/status", async (AccountService accounts, CancellationToken ct) =>
            Results.Ok(new { setupRequired = await accounts.IsSetupRequiredAsync(ct) })).AllowAnonymous();

        // Bound from the form so the antiforgery token of the layout's logout button is validated.
        app.MapPost("/logout", async (HttpContext http, [FromForm] string? returnUrl) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });
    }

    private static async Task<IResult> CliLoginAsync(CliLoginRequest request, CliLoginService login, CancellationToken ct)
    {
        try
        {
            var result = await login.LoginAsync(request, ct);
            return result is null
                ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Empresa, usuario o contraseña incorrectos.")
                : Results.Ok(result);
        }
        catch (ValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. ex.Errors] });
        }
    }

    private static async Task<IResult> IngestRunAsync(
        HttpRequest http,
        IngestRunRequest request,
        ApiKeyService apiKeys,
        RunIngestionService ingestion,
        CancellationToken ct)
    {
        var organizationId = await apiKeys.AuthenticateAsync(http.Headers[ApiKeyHeader].FirstOrDefault(), ct);
        if (organizationId is null)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: $"Missing or invalid API key (header {ApiKeyHeader}).");

        try
        {
            var result = await ingestion.IngestAsync(organizationId.Value, request, ct);
            return Results.Created($"/solutions/{result.Solution}?run={result.RunId}", result);
        }
        catch (ValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. ex.Errors] });
        }
    }
}
