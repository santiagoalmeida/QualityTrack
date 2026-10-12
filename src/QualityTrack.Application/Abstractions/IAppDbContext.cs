using Microsoft.EntityFrameworkCore;
using QualityTrack.Domain.Entities;

namespace QualityTrack.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<User> Users { get; }
    DbSet<ApiKey> ApiKeys { get; }
    DbSet<Solution> Solutions { get; }
    DbSet<Project> Projects { get; }
    DbSet<Run> Runs { get; }
    DbSet<Measurement> Measurements { get; }
    DbSet<Goal> Goals { get; }
    DbSet<Finding> Findings { get; }
    DbSet<Rule> Rules { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
