using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Abstractions;
using QualityTrack.Application.Common;
using QualityTrack.Domain.Entities;

namespace QualityTrack.Application.ApiKeys;

public sealed record ApiKeyInfo(Guid Id, string Name, string Prefix, DateTime CreatedAt, DateTime? LastUsedAt, DateTime? RevokedAt);

/// <param name="Secret">The full key. It is returned only here: afterwards just its hash exists.</param>
public sealed record CreatedApiKey(ApiKeyInfo Info, string Secret);

public class ApiKeyService(IAppDbContext db, TimeProvider clock)
{
    private const string KeyPrefix = "qt_";
    private const int VisiblePrefixLength = 10;

    public async Task<IReadOnlyList<ApiKeyInfo>> ListAsync(Guid organizationId, CancellationToken ct = default) =>
        (await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.OrganizationId == organizationId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct))
        .Select(ToInfo)
        .ToList();

    public async Task<CreatedApiKey> CreateAsync(Guid organizationId, string? name, CancellationToken ct = default)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 80)
            throw new ValidationException("Ponle un nombre a la clave (hasta 80 caracteres).");

        var secret = KeyPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var key = new ApiKey
        {
            OrganizationId = organizationId,
            Name = name,
            Prefix = secret[..VisiblePrefixLength],
            KeyHash = Hash(secret),
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        return new CreatedApiKey(ToInfo(key), secret);
    }

    public async Task RevokeAsync(Guid organizationId, Guid keyId, CancellationToken ct = default)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.OrganizationId == organizationId && k.Id == keyId, ct);
        if (key is null || key.RevokedAt is not null)
            return;

        key.RevokedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Returns the organization a key belongs to, or null if the key is unknown or revoked.</summary>
    public async Task<Guid?> AuthenticateAsync(string? secret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secret) || !secret.StartsWith(KeyPrefix, StringComparison.Ordinal))
            return null;

        var hash = Hash(secret.Trim());
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash && k.RevokedAt == null, ct);
        if (key is null)
            return null;

        key.LastUsedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return key.OrganizationId;
    }

    // Keys are 192 random bits, so a plain SHA-256 is enough: there is nothing to brute-force.
    private static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static ApiKeyInfo ToInfo(ApiKey key) => new(key.Id, key.Name, key.Prefix, key.CreatedAt, key.LastUsedAt, key.RevokedAt);
}
