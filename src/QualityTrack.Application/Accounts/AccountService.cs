using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Abstractions;
using QualityTrack.Application.Common;
using QualityTrack.Domain.Entities;

namespace QualityTrack.Application.Accounts;

public sealed record AuthenticatedUser(Guid UserId, Guid OrganizationId, string OrganizationName, string Email, string DisplayName, UserRole Role);

public sealed record SetupRequest(string? OrganizationName, string? DisplayName, string? Email, string? Password);

public class AccountService(IAppDbContext db, IPasswordHasher hasher, TimeProvider clock)
{
    public const int MinPasswordLength = 10;

    /// <summary>True until the first organization and its administrator exist.</summary>
    public async Task<bool> IsSetupRequiredAsync(CancellationToken ct = default) => !await db.Organizations.AnyAsync(ct);

    public async Task<AuthenticatedUser> SetupAsync(SetupRequest request, CancellationToken ct = default)
    {
        if (!await IsSetupRequiredAsync(ct))
            throw new ValidationException("La instalación ya fue configurada.");

        var organizationName = request.OrganizationName?.Trim() ?? "";
        var displayName = request.DisplayName?.Trim() ?? "";
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var errors = new List<string>();
        if (organizationName.Length is 0 or > 120)
            errors.Add("Indica el nombre de la organización.");
        if (displayName.Length is 0 or > 120)
            errors.Add("Indica tu nombre.");
        if (email.Length > 254 || !email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
            errors.Add("Indica un correo válido.");
        if ((request.Password?.Length ?? 0) < MinPasswordLength)
            errors.Add($"La contraseña debe tener al menos {MinPasswordLength} caracteres.");
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var now = clock.GetUtcNow().UtcDateTime;
        var organization = new Organization { Name = organizationName, CreatedAt = now };
        var user = new User
        {
            Email = email,
            DisplayName = displayName,
            PasswordHash = hasher.Hash(request.Password!),
            Role = UserRole.Admin,
            CreatedAt = now
        };
        organization.Users.Add(user);
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(ct);

        return new AuthenticatedUser(user.Id, organization.Id, organization.Name, user.Email, user.DisplayName, user.Role);
    }

    public async Task<AuthenticatedUser?> SignInAsync(string? email, string? password, CancellationToken ct = default)
    {
        email = email?.Trim().ToLowerInvariant() ?? "";
        var user = await db.Users.AsNoTracking().Include(u => u.Organization).FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || string.IsNullOrEmpty(password) || !hasher.Verify(password, user.PasswordHash))
            return null;

        return new AuthenticatedUser(user.Id, user.OrganizationId, user.Organization!.Name, user.Email, user.DisplayName, user.Role);
    }
}
