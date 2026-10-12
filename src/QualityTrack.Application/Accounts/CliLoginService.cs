using QualityTrack.Application.ApiKeys;
using QualityTrack.Application.Common;

namespace QualityTrack.Application.Accounts;

/// <summary>Payload of <c>POST /api/v1/cli/login</c>.</summary>
public sealed record CliLoginRequest
{
    /// <summary>Organization name. On a fresh installation it names the organization that gets created.</summary>
    public string? Organization { get; init; }
    public string? Email { get; init; }
    public string? Password { get; init; }
    /// <summary>Label of the API key that is issued, e.g. the machine name.</summary>
    public string? KeyName { get; init; }
}

/// <param name="ApiKey">The issued key. It is shown only here; the caller stores it instead of the password.</param>
/// <param name="OrganizationCreated">True when this call set up the installation.</param>
public sealed record CliLoginResult(string ApiKey, string Organization, string Email, bool OrganizationCreated);

/// <summary>
/// Lets a script trade organization, user and password for an API key, so the password never has to be
/// stored. On an installation that has not been set up, the same call creates the organization and its
/// administrator - exactly what the setup page does.
/// </summary>
public class CliLoginService(AccountService accounts, ApiKeyService apiKeys)
{
    /// <returns>Null when the organization, user or password do not match.</returns>
    public async Task<CliLoginResult?> LoginAsync(CliLoginRequest request, CancellationToken ct = default)
    {
        AuthenticatedUser? user;
        var created = false;
        if (await accounts.IsSetupRequiredAsync(ct))
        {
            var email = request.Email?.Trim() ?? "";
            var displayName = email.Contains('@') ? email[..email.IndexOf('@')] : email;
            user = await accounts.SetupAsync(new SetupRequest(request.Organization, displayName, email, request.Password), ct);
            created = true;
        }
        else
        {
            user = await accounts.SignInAsync(request.Email, request.Password, ct);
            // One answer for every mismatch, so the call does not reveal which part was wrong.
            if (user is null || !string.Equals(user.OrganizationName, request.Organization?.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;
        }

        var keyName = string.IsNullOrWhiteSpace(request.KeyName) ? "CLI" : request.KeyName.Trim();
        var key = await apiKeys.CreateAsync(user.OrganizationId, keyName.Length > 80 ? keyName[..80] : keyName, ct);
        return new CliLoginResult(key.Secret, user.OrganizationName, user.Email, created);
    }
}
