using Microsoft.EntityFrameworkCore;
using QualityTrack.Application.Abstractions;
using QualityTrack.Domain.Entities;

namespace QualityTrack.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Solution> Solutions => Set<Solution>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<Rule> Rules => Set<Rule>();

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

        modelBuilder.Entity<Solution>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => new { x.OrganizationId, x.Key }).IsUnique();
            e.HasOne(x => x.Organization).WithMany(o => o.Solutions).HasForeignKey(x => x.OrganizationId);
        });

        modelBuilder.Entity<Project>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => new { x.SolutionId, x.Key }).IsUnique();
            e.HasOne(x => x.Solution).WithMany(p => p.Projects).HasForeignKey(x => x.SolutionId);
        });

        modelBuilder.Entity<Run>(e =>
        {
            e.Property(x => x.Branch).HasMaxLength(200);
            e.Property(x => x.CommitSha).HasMaxLength(64);
            e.Property(x => x.Source).HasMaxLength(80);
            e.HasIndex(x => new { x.SolutionId, x.Branch, x.ExecutedAt });
            e.HasOne(x => x.Solution).WithMany(p => p.Runs).HasForeignKey(x => x.SolutionId);
        });

        modelBuilder.Entity<Measurement>(e =>
        {
            e.Property(x => x.MetricKey).HasMaxLength(80);
            e.HasIndex(x => new { x.RunId, x.ProjectId, x.MetricKey }).IsUnique();
            e.HasIndex(x => new { x.ProjectId, x.MetricKey });
            e.HasOne(x => x.Run).WithMany(r => r.Measurements).HasForeignKey(x => x.RunId);
            // The run already cascades from the solution; a second cascade path through the project is refused by some databases.
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Goal>(e =>
        {
            e.Property(x => x.MetricKey).HasMaxLength(80);
            e.HasIndex(x => new { x.ProjectId, x.MetricKey }).IsUnique();
            e.HasOne(x => x.Solution).WithMany(p => p.Goals).HasForeignKey(x => x.SolutionId);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Finding>(e =>
        {
            e.Property(x => x.MetricKey).HasMaxLength(80);
            e.Property(x => x.RuleKey).HasMaxLength(200);
            e.Property(x => x.Severity).HasMaxLength(40);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.FilePath).HasMaxLength(1000);
            e.HasIndex(x => new { x.RunId, x.ProjectId, x.MetricKey, x.RuleKey });
            e.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Rule>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(500);
            e.HasIndex(x => new { x.SolutionId, x.Key }).IsUnique();
            e.HasOne(x => x.Solution).WithMany().HasForeignKey(x => x.SolutionId);
        });
    }
}
