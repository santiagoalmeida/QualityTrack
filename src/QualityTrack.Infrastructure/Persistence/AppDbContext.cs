using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Abstractions;
using QualityTrack.Domain.Entities;

namespace QualityTrack.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<Goal> Goals => Set<Goal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Ids are assigned in the constructors. Telling EF it never generates them makes an entity that
        // is reached through a navigation of an already-loaded parent count as new instead of as modified.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            modelBuilder.Entity(entityType.ClrType).Property(nameof(Organization.Id)).ValueGeneratedNever();

        modelBuilder.Entity<Organization>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.DisplayName).HasMaxLength(120);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne(x => x.Organization).WithMany(o => o.Users).HasForeignKey(x => x.OrganizationId);
        });

        modelBuilder.Entity<ApiKey>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Prefix).HasMaxLength(16);
            e.Property(x => x.KeyHash).HasMaxLength(64);
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.HasOne(x => x.Organization).WithMany(o => o.ApiKeys).HasForeignKey(x => x.OrganizationId);
        });

        modelBuilder.Entity<Project>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => new { x.OrganizationId, x.Key }).IsUnique();
            e.HasOne(x => x.Organization).WithMany(o => o.Projects).HasForeignKey(x => x.OrganizationId);
        });

        modelBuilder.Entity<Component>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => new { x.ProjectId, x.Key }).IsUnique();
            e.HasOne(x => x.Project).WithMany(p => p.Components).HasForeignKey(x => x.ProjectId);
        });

        modelBuilder.Entity<Run>(e =>
        {
            e.Property(x => x.Branch).HasMaxLength(200);
            e.Property(x => x.CommitSha).HasMaxLength(64);
            e.Property(x => x.Source).HasMaxLength(80);
            e.HasIndex(x => new { x.ProjectId, x.Branch, x.ExecutedAt });
            e.HasOne(x => x.Project).WithMany(p => p.Runs).HasForeignKey(x => x.ProjectId);
        });

        modelBuilder.Entity<Measurement>(e =>
        {
            e.Property(x => x.MetricKey).HasMaxLength(80);
            e.HasIndex(x => new { x.RunId, x.ComponentId, x.MetricKey }).IsUnique();
            e.HasIndex(x => new { x.ComponentId, x.MetricKey });
            e.HasOne(x => x.Run).WithMany(r => r.Measurements).HasForeignKey(x => x.RunId);
            // The run already cascades from the project; a second cascade path through the component is refused by some databases.
            e.HasOne(x => x.Component).WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Goal>(e =>
        {
            e.Property(x => x.MetricKey).HasMaxLength(80);
            e.HasIndex(x => new { x.ComponentId, x.MetricKey }).IsUnique();
            e.HasOne(x => x.Project).WithMany(p => p.Goals).HasForeignKey(x => x.ProjectId);
            e.HasOne(x => x.Component).WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
