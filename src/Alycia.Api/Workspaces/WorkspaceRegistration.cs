using Alycia.Modules.Workspaces.Infrastructure;
using Alycia.Modules.Workspaces.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alycia.Api.Workspaces;

internal static class WorkspaceRegistration
{
    public static void AddAlyciaWorkspaces(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<WorkspacesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "workspaces")));
        services.AddScoped<IWorkspaceRepository, EfWorkspaceRepository>();
        services.AddScoped<CreateWorkspaceService>();
        services.AddScoped<GetWorkspaceService>();
    }
}
