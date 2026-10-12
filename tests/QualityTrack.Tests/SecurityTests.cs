using QualityTrack.Application.Accounts;
using QualityTrack.Application.ApiKeys;
using QualityTrack.Application.Common;
using QualityTrack.Infrastructure.Security;

namespace QualityTrack.Tests;

public sealed class SecurityTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ApiKey_AuthenticatesUntilRevokedAndIsStoredHashed()
    {
        var organizationId = _database.AddOrganization();
        await using var db = _database.NewContext();
        var keys = new ApiKeyService(db, _clock);

        var created = await keys.CreateAsync(organizationId, " CI ");

        Assert.StartsWith("qt_", created.Secret);
        Assert.Equal("CI", created.Info.Name);
        Assert.DoesNotContain(db.ApiKeys, k => k.KeyHash == created.Secret);
        Assert.Equal(organizationId, await keys.AuthenticateAsync(created.Secret));
        Assert.NotNull((await keys.ListAsync(organizationId)).Single().LastUsedAt);
        Assert.Null(await keys.AuthenticateAsync(created.Secret + "x"));
        Assert.Null(await keys.AuthenticateAsync(null));

        await keys.RevokeAsync(organizationId, created.Info.Id);

        Assert.Null(await keys.AuthenticateAsync(created.Secret));
    }

    [Fact]
    public async Task ApiKey_CannotBeRevokedFromAnotherOrganization()
    {
        var owner = _database.AddOrganization();
        var stranger = _database.AddOrganization("Otra");
        await using var db = _database.NewContext();
        var keys = new ApiKeyService(db, _clock);
        var created = await keys.CreateAsync(owner, "CI");

        await keys.RevokeAsync(stranger, created.Info.Id);

        Assert.Equal(owner, await keys.AuthenticateAsync(created.Secret));
    }

    [Fact]
    public async Task Setup_CreatesTheFirstAdminOnceAndSignInChecksThePassword()
    {
        await using var db = _database.NewContext();
        var accounts = new AccountService(db, new Pbkdf2PasswordHasher(), _clock);
        Assert.True(await accounts.IsSetupRequiredAsync());

        var invalid = await Assert.ThrowsAsync<ValidationException>(() => accounts.SetupAsync(new SetupRequest("", "", "nope", "short")));
        Assert.Equal(4, invalid.Errors.Count);

        var admin = await accounts.SetupAsync(new SetupRequest("Acme", "Ana", " Ana@Acme.io ", "correct horse"));

        Assert.False(await accounts.IsSetupRequiredAsync());
        Assert.Equal("ana@acme.io", admin.Email);
        Assert.Equal(admin.UserId, (await accounts.SignInAsync("ANA@acme.io", "correct horse"))!.UserId);
        Assert.Null(await accounts.SignInAsync("ana@acme.io", "wrong password"));
        Assert.Null(await accounts.SignInAsync("nobody@acme.io", "correct horse"));
        await Assert.ThrowsAsync<ValidationException>(() => accounts.SetupAsync(new SetupRequest("Otra", "Eva", "eva@otra.io", "another password")));
    }
}
