using DnnManager.Application.Abstractions;
using DnnManager.Application.Configuration;
using DnnManager.Domain;
using DnnManager.Infrastructure.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DnnManager.Infrastructure.Docker;

public sealed class DockerService : IDockerService
{
    private readonly ProcessRunner _proc;
    private readonly ILogger<DockerService> _log;

    public DockerService(ProcessRunner proc, ILogger<DockerService> log)
    {
        _proc = proc; _log = log;
    }

    public async Task<bool> IsContainerRunningAsync(string containerName, CancellationToken ct)
    {
        var r = await _proc.RunAsync("docker",
            new[] { "ps", "--filter", $"name=^{containerName}$", "--format", "{{.Names}}" }, ct);
        return r.Success && r.StdOut.Trim() == containerName;
    }

    public async Task<bool> DoesContainerExistAsync(string containerName, CancellationToken ct)
    {
        var r = await _proc.RunAsync("docker",
            new[] { "ps", "-a", "--filter", $"name=^{containerName}$", "--format", "{{.Names}}" }, ct);
        return r.Success && r.StdOut.Trim() == containerName;
    }

    public async Task<Result> StartContainerAsync(string containerName, CancellationToken ct)
    {
        var r = await _proc.RunAsync("docker", new[] { "start", containerName }, ct);
        return r.Success ? Result.Ok() : Result.Fail($"docker start failed: {r.StdErr}");
    }

    public async Task<int?> GetPublishedPortAsync(string containerName, CancellationToken ct)
    {
        var r = await _proc.RunAsync("docker", new[] { "port", containerName, "1433/tcp" }, ct);
        if (!r.Success) return null;
        var line = r.StdOut.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line is null) return null;
        var idx = line.LastIndexOf(':');
        if (idx < 0) return null;
        return int.TryParse(line[(idx + 1)..].Trim(), out var port) ? port : null;
    }

    public async Task<Result> ComposeUpAsync(string composeFile, string envFile, string projectName, CancellationToken ct)
    {
        var r = await _proc.RunAsync("docker",
            new[] { "compose", "-f", composeFile, "--env-file", envFile, "-p", projectName, "up", "-d" }, ct);
        return r.Success ? Result.Ok() : Result.Fail($"docker compose up failed: {r.StdErr}");
    }

    public async Task<Result> ComposeDownAsync(string composeFile, string envFile, bool removeVolumes, CancellationToken ct)
    {
        var args = new List<string> { "compose", "-f", composeFile, "--env-file", envFile, "down" };
        if (removeVolumes) args.Add("-v");
        var r = await _proc.RunAsync("docker", args, ct);
        return r.Success ? Result.Ok() : Result.Fail($"docker compose down failed: {r.StdErr}");
    }

    public async Task<Result<string>> ExecAsync(string containerName, IReadOnlyList<string> args, CancellationToken ct)
    {
        var full = new List<string> { "exec", containerName };
        full.AddRange(args);
        var r = await _proc.RunAsync("docker", full, ct);
        return r.Success ? Result<string>.Ok(r.StdOut) : Result<string>.Fail(r.StdErr.Length > 0 ? r.StdErr : r.StdOut);
    }
}

public sealed class DockerConfigWriter : IDockerConfigWriter
{
    private readonly AppOptions _opts;

    public DockerConfigWriter(IOptions<AppOptions> opts) => _opts = opts.Value;

    public async Task WriteAsync(DnnProject project, int sqlPort, CancellationToken ct)
    {
        Directory.CreateDirectory(project.DockerDirectory);
        // Backup directory is intentionally NOT created here - it's created on
        // demand by the first backup so unused projects stay tidy.

        var d = _opts.Docker;
        var env = $"""
MSSQL_PID={d.MssqlPid}
MSSQL_PASSWORD={d.SaPassword}
MSSQL_COLLATION={d.Collation}
SQLSERVER_PORT={sqlPort}

DB_NAME={project.Name}{d.DefaultDbNameSuffix}
DB_USER={project.Name}{d.DefaultDbUserSuffix}
DB_PASSWORD={d.DefaultDbPassword}
DB_COLLATION={d.Collation}
""";
        var compose = $"""
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: {d.ContainerName}
    hostname: {d.ContainerName}
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD={d.SaPassword}
      - MSSQL_SA_PASSWORD={d.SaPassword}
      - MSSQL_PID={d.MssqlPid}
      - MSSQL_COLLATION={d.Collation}
    ports:
      - "{sqlPort}:1433"
    volumes:
      - {d.VolumeName}:/var/opt/mssql
    restart: unless-stopped
    networks:
      - dnn_network
    healthcheck:
      test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P {d.SaPassword} -C -No -Q "SELECT 1"
      interval: 30s
      timeout: 10s
      retries: 5
      start_period: 60s

volumes:
  {d.VolumeName}:
    name: {d.VolumeName}

networks:
  dnn_network:
    driver: bridge
    name: dnn_network
""";
        var utf8 = new System.Text.UTF8Encoding(false);
        await File.WriteAllTextAsync(project.EnvFile, env, utf8, ct);
        await File.WriteAllTextAsync(project.ComposeFile, compose, utf8, ct);
    }
}
