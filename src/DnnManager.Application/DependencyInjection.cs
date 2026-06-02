using DnnManager.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace DnnManager.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<SetupProjectUseCase>();
        services.AddScoped<RemoveProjectUseCase>();
        services.AddScoped<ListProjectsUseCase>();
        services.AddScoped<CheckPrerequisitesUseCase>();
        services.AddScoped<ExportDatabaseUseCase>();
        services.AddScoped<ImportDatabaseUseCase>();
        services.AddScoped<CloneProjectUseCase>();
        return services;
    }
}
