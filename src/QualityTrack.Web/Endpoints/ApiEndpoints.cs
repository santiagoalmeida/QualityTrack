using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using QualityTrack.Application.ApiKeys;
using QualityTrack.Application.Common;
using QualityTrack.Application.Ingestion;

namespace QualityTrack.Web.Endpoints;

public static class ApiEndpoints
{
    public const string ApiKeyHeader = "X-Api-Key";

    public static void MapQualityTrackEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

        app.MapPost("/api/v1/runs", IngestRunAsync).AllowAnonymous().DisableAntiforgery();

        // Bound from the form so the antiforgery token of the layout's logout button is validated.
        app.MapPost("/logout", async (HttpContext http, [FromForm] string? returnUrl) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });
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
            return Results.Created($"/projects/{result.Project}?run={result.RunId}", result);
        }
        catch (ValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [.. ex.Errors] });
        }
    }
}
