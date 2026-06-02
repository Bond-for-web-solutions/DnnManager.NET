using DnnManager.Application.Abstractions;
using DnnManager.Application.Configuration;
using DnnManager.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DnnManager.Application.UseCases;

public sealed class SetupProjectRequest
{
    public required string ProjectName { get; init; }
    public required string ReleaseApiUrl { get; init; }
    public string? Version { get; init; }
}

public sealed class SetupProjectUseCase
{
    private readonly AppOptions _opts;
    private readonly IProjectRepository _projects;
    private readonly IDnnReleaseService _releases;
    private readonly IDnnPackageInstaller _installer;
    private readonly IIisManager _iis;
    private readonly IDockerService _docker;
    private readonly IDockerConfigWriter _dockerCfg;
    private readonly ISqlServerService _sql;
    private readonly IEnvFileService _envFiles;
    private readonly IHttpConnectivityChecker _http;
    private readonly IPrerequisiteChecker _prereq;
    private readonly IUserPrompt _prompt;
    private readonly ILogger<SetupProjectUseCase> _log;

    public SetupProjectUseCase(
        IOptions<AppOptions> opts,
        IProjectRepository projects,
        IDnnReleaseService releases,
        IDnnPackageInstaller installer,
        IIisManager iis,
        IDockerService docker,
        IDockerConfigWriter dockerCfg,
        ISqlServerService sql,
        IEnvFileService envFiles,
        IHttpConnectivityChecker http,
        IPrerequisiteChecker prereq,
        IUserPrompt prompt,
        ILogger<SetupProjectUseCase> log)
    {
        _opts = opts.Value;
        _projects = projects;
        _releases = releases;
        _installer = installer;
        _iis = iis;
        _docker = docker;
        _dockerCfg = dockerCfg;
        _sql = sql;
        _envFiles = envFiles;
        _http = http;
        _prereq = prereq;
        _prompt = prompt;
        _log = log;
    }

    public async Task<Result> ExecuteAsync(SetupProjectRequest req, IProgressReporter reporter, CancellationToken ct)
    {
        try
        {
            reporter.Step("Step 1: Prerequisites");
            var docker = await _prereq.CheckDockerAsync(reporter, ct);
            if (!docker.Success) return docker;
            await _prereq.EnsureIisFeaturesAsync(reporter, _prompt, ct);

            var project = _projects.Build(req.ProjectName);
            var hostname = $"{req.ProjectName}.{_opts.HostnameSuffix}";

            reporter.Step("Step 2: Project directory");
            Directory.CreateDirectory(_opts.BaseDirectory);
            if (Directory.Exists(project.ProjectDirectory))
            {
                reporter.Info($"Project directory already exists: {project.ProjectDirectory}");
                if (!await _prompt.ConfirmAsync("Directory exists. Continue and overwrite?", false, ct))
                    return Result.Fail("Aborted by user.");
            }
            Directory.CreateDirectory(project.ProjectDirectory);
            reporter.Success($"Project directory ready: {project.ProjectDirectory}");

            reporter.Step("Step 3: Determine DNN version");
            var releaseResult = await _releases.GetReleaseAsync(req.ReleaseApiUrl, req.Version, ct);
            if (!releaseResult.Success || releaseResult.Value is null)
                return Result.Fail(releaseResult.Error ?? "Could not resolve a DNN release.");
            var release = releaseResult.Value;
            reporter.Success($"Using DNN {release.Version} ({release.DownloadUrl})");

            reporter.Step("Step 4: Download & extract");
            var extract = await _installer.DownloadAndExtractAsync(release, project.ProjectDirectory, reporter, ct);
            if (!extract.Success) return extract;

            reporter.Step("Step 5: IIS website");
            _iis.RemoveSite(req.ProjectName);
            var create = _iis.CreateSite(req.ProjectName, project.ProjectDirectory, hostname, _opts.SitePort);
            if (!create.Success) return create;
            _iis.GrantPermissions(project.ProjectDirectory, new[]
            {
                "IIS_IUSRS",
                "IUSR",
                $"IIS APPPOOL\\{req.ProjectName}"
            });
            reporter.Success($"IIS site '{req.ProjectName}' bound to http://{hostname}");

            reporter.Step("Step 6: Docker config + SQL Server");
            var containerName = _opts.Docker.ContainerName;
            var containerExists = await _docker.DoesContainerExistAsync(containerName, ct);
            var containerRunning = containerExists
                && await _docker.IsContainerRunningAsync(containerName, ct);

            int port;
            if (containerExists)
            {
                // Reuse the existing shared SQL Server container instead of failing
                // with a name conflict on `docker compose up`. Just provision a new
                // database/user inside it for this project.
                if (!containerRunning)
                {
                    reporter.Info($"Container '{containerName}' exists but is stopped \u2014 starting it.");
                    var start = await _docker.StartContainerAsync(containerName, ct);
                    if (!start.Success) return start;
                }
                else
                {
                    reporter.Info($"Reusing running SQL Server container '{containerName}'.");
                }

                var existingPort = await _docker.GetPublishedPortAsync(containerName, ct);
                if (existingPort is null)
                    return Result.Fail($"Could not determine published port for container '{containerName}'.");
                port = existingPort.Value;
                await _dockerCfg.WriteAsync(project, port, ct);
            }
            else
            {
                port = FindFreePort(_opts.Docker.DefaultPort);
                await _dockerCfg.WriteAsync(project, port, ct);

                var up = await _docker.ComposeUpAsync(project.ComposeFile, project.EnvFile, "dnn-shared", ct);
                if (!up.Success) return up;
            }

            var ready = await _sql.WaitReadyAsync(180, reporter, ct);
            if (!ready.Success) return ready;

            var db = new DatabaseConfig(
                Server: $"localhost,{port}",
                DatabaseName: req.ProjectName + _opts.Docker.DefaultDbNameSuffix,
                User: req.ProjectName + _opts.Docker.DefaultDbUserSuffix,
                Password: _opts.Docker.DefaultDbPassword,
                Collation: _opts.Docker.Collation,
                Port: port,
                BackupDirectory: project.BackupDirectory);

            var exists = await _sql.DatabaseExistsAsync(db.DatabaseName, ct);
            if (exists.Success && exists.Value &&
                await _prompt.ConfirmAsync($"Database '{db.DatabaseName}' exists. Drop and recreate?", false, ct))
            {
                await _sql.DropDatabaseAndUserAsync(db, ct);
            }
            await _sql.CreateDatabaseAndUserAsync(db, ct);
            await _envFiles.EnsureDeveloperEnvAsync(project, db, ct);
            await _envFiles.EnsureProductionEnvAsync(project, ct);

            reporter.Step("Step 7: Start site & verify");
            _iis.StartSite(req.ProjectName);
            var http = await _http.CheckAsync($"http://{hostname}", 15, ct);
            if (http.Success)
                reporter.Success($"HTTP {http.Value} from http://{hostname}");
            else
                reporter.Info($"HTTP probe: {http.Error} (expected before install wizard runs)");

            reporter.Step("Setup complete");
            reporter.Success($"Open http://{hostname} to complete the DNN Installation Wizard.");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Setup failed for {Project}", req.ProjectName);
            return Result.Fail(ex.Message);
        }
    }

    private static int FindFreePort(int preferred)
    {
        try
        {
            using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, preferred);
            l.Start(); l.Stop();
            return preferred;
        }
        catch
        {
            using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            var p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }
    }
}
