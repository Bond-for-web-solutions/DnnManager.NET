using DnnManager.Application.Abstractions;
using DnnManager.Application.Configuration;
using DnnManager.Domain;
using Microsoft.Extensions.Options;

namespace DnnManager.Application.UseCases;

public sealed class ListProjectsUseCase
{
    private readonly AppOptions _opts;
    private readonly IProjectRepository _projects;
    private readonly IIisManager _iis;
    private readonly IDockerService _docker;
    private readonly IEnvFileService _envFiles;

    public ListProjectsUseCase(
        IOptions<AppOptions> opts,
        IProjectRepository projects,
        IIisManager iis,
        IDockerService docker,
        IEnvFileService envFiles)
    {
        _opts = opts.Value;
        _projects = projects;
        _iis = iis;
        _docker = docker;
        _envFiles = envFiles;
    }

    public async Task<IReadOnlyList<ProjectStatus>> ExecuteAsync(CancellationToken ct)
    {
        var list = new List<ProjectStatus>();
        var containerRunning = await _docker.IsContainerRunningAsync(_opts.Docker.ContainerName, ct);

        foreach (var name in _projects.ListAllProjectDirectories())
        {
            var p = _projects.Build(name);
            var env = File.Exists(p.EnvFile) ? _envFiles.Read(p.EnvFile) : (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
            long size = 0;
            try
            {
                if (Directory.Exists(p.ProjectDirectory))
                    size = new DirectoryInfo(p.ProjectDirectory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { /* ignore */ }

            int? port = null;
            if (env.TryGetValue("SQLSERVER_PORT", out var portStr) && int.TryParse(portStr, out var pn)) port = pn;

            list.Add(new ProjectStatus(
                name,
                p.ProjectDirectory,
                _iis.SiteExists(name),
                _iis.GetSiteState(name),
                size,
                containerRunning,
                env.GetValueOrDefault("DB_NAME"),
                env.GetValueOrDefault("DB_USER"),
                port));
        }
        return list;
    }
}
