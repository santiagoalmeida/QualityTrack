using Microsoft.EntityFrameworkCore;
using QualityTrack.Domain.Entities;

namespace QualityTrack.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<User> Users { get; }
    DbSet<ApiKey> ApiKeys { get; }
    DbSet<Project> Projects { get; }
    DbSet<Component> Components { get; }
    DbSet<Run> Runs { get; }
    DbSet<Measurement> Measurements { get; }
    DbSet<Goal> Goals { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
