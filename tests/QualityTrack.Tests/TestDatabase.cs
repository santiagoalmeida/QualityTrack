using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QualityTrack.Domain.Entities;
using QualityTrack.Infrastructure.Persistence;

namespace QualityTrack.Tests;

/// <summary>In-memory SQLite database that lives as long as the test.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDatabase()
    {
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    public Guid AddOrganization(string name = "Acme")
    {
        using var db = NewContext();
        var organization = new Organization { Name = name, CreatedAt = DateTime.UtcNow };
        db.Organizations.Add(organization);
        db.SaveChanges();
        return organization.Id;
    }

    public void Dispose() => _connection.Dispose();
}

public sealed class FixedClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
