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
    private readonly IWebConfigService _webConfig;

    public ListProjectsUseCase(
        IOptions<AppOptions> opts,
        IProjectRepository projects,
        IIisManager iis,
        IDockerService docker,
        IWebConfigService webConfig)
    {
        _opts = opts.Value;
        _projects = projects;
        _iis = iis;
        _docker = docker;
        _webConfig = webConfig;
    }

    public async Task<IReadOnlyList<ProjectStatus>> ExecuteAsync(CancellationToken ct)
    {
        var list = new List<ProjectStatus>();
        var containerRunning = await _docker.IsContainerRunningAsync(_opts.Docker.ContainerName, ct);
        // One shared SQL container, so its published port (when up) applies to every project.
        int? sqlPort = containerRunning ? await _docker.GetPublishedPortAsync(_opts.Docker.ContainerName, ct) : null;

        foreach (var name in _projects.ListAllProjectDirectories())
        {
            var p = _projects.Build(name);
            long size = 0;
            try
            {
                if (Directory.Exists(p.ProjectDirectory))
                    size = new DirectoryInfo(p.ProjectDirectory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { /* ignore */ }

            // The database the site uses comes from its web.config; before the DNN wizard wires that
            // up, fall back to the conventional {project}_dnndev name setup creates. The site always
            // connects as the container sa.
            var dbName = DeveloperDb.FromWebConfig(p, _webConfig) ?? (name + _opts.Docker.DefaultDbNameSuffix);

            var siteUrl = $"http://{name}.{_opts.HostnameSuffix}";
            if (_opts.SitePort != 80) siteUrl += $":{_opts.SitePort}";

            list.Add(new ProjectStatus(
                name,
                p.ProjectDirectory,
                _iis.SiteExists(name),
                _iis.GetSiteState(name),
                size,
                containerRunning,
                dbName,
                "sa",
                sqlPort,
                siteUrl));
        }
        return list;
    }
}
