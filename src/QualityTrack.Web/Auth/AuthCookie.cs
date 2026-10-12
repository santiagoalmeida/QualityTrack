using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using QualityTrack.Application.Accounts;

namespace QualityTrack.Web.Auth;

public static class AuthCookie
{
    private const string OrganizationIdClaim = "qt:org";
    private const string OrganizationNameClaim = "qt:org_name";

    public static Task SignInAsync(HttpContext http, AuthenticatedUser user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(OrganizationIdClaim, user.OrganizationId.ToString()),
            new Claim(OrganizationNameClaim, user.OrganizationName)
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    /// <summary>Tenant of the signed-in user; every query made on their behalf is scoped to it.</summary>
    public static Guid OrganizationId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(OrganizationIdClaim)!);

    public static string OrganizationName(this ClaimsPrincipal user) => user.FindFirstValue(OrganizationNameClaim) ?? "";

    /// <summary>Only same-site paths are accepted as a post-login destination.</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";
}
