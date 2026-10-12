using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QualityTrack.Application.Accounts;
using QualityTrack.Application.ApiKeys;
using QualityTrack.Application.Dashboard;
using QualityTrack.Application.Findings;
using QualityTrack.Application.Goals;
using QualityTrack.Application.Ingestion;

namespace QualityTrack.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<RunIngestionService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<GoalService>();
        services.AddScoped<FindingsService>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<AccountService>();
        services.AddScoped<CliLoginService>();
        return services;
    }
}
