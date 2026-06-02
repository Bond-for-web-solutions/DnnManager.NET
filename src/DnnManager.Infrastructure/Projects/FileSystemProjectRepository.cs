using DnnManager.Application.Abstractions;
using DnnManager.Application.Configuration;
using DnnManager.Domain;
using Microsoft.Extensions.Options;

namespace DnnManager.Infrastructure.Projects;

public sealed class FileSystemProjectRepository : IProjectRepository
{
    private readonly AppOptions _opts;

    public FileSystemProjectRepository(IOptions<AppOptions> opts) => _opts = opts.Value;

    public DnnProject Build(string projectName)
    {
        var projectDir = Path.Combine(_opts.BaseDirectory, projectName);
        var dockerDir = Path.Combine(projectDir, "workspace", "docker");
        var backupDir = Path.Combine(projectDir, "workspace", "backups");
        return new DnnProject(
            projectName,
            projectDir,
            dockerDir,
            Path.Combine(dockerDir, "docker-compose.yml"),
            Path.Combine(dockerDir, ".env"),
            Path.Combine(dockerDir, ".env.developer"),
            Path.Combine(dockerDir, ".env.production"),
            backupDir);
    }

    public bool ProjectExists(string projectName)
        => Directory.Exists(Path.Combine(_opts.BaseDirectory, projectName));

    public IReadOnlyList<string> ListConfiguredProjects()
    {
        if (!Directory.Exists(_opts.BaseDirectory)) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(_opts.BaseDirectory))
        {
            var compose = Path.Combine(dir, "workspace", "docker", "docker-compose.yml");
            var env = Path.Combine(dir, "workspace", "docker", ".env");
            if (File.Exists(compose) || File.Exists(env))
                list.Add(Path.GetFileName(dir)!);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public IReadOnlyList<string> ListAllProjectDirectories()
    {
        if (!Directory.Exists(_opts.BaseDirectory)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(_opts.BaseDirectory)
            .Select(d => Path.GetFileName(d)!)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class EnvFileService : IEnvFileService
{
    private readonly AppOptions _opts;
    public EnvFileService(IOptions<AppOptions> opts) => _opts = opts.Value;

    public IReadOnlyDictionary<string, string> Read(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return dict;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var i = line.IndexOf('=');
            if (i <= 0) continue;
            dict[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        return dict;
    }

    public async Task WriteAsync(string path, IReadOnlyDictionary<string, string> values, string? header, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(header)) sb.AppendLine($"# {header}");
        foreach (var kv in values) sb.AppendLine($"{kv.Key}={kv.Value}");
        var utf8 = new System.Text.UTF8Encoding(false);
        await File.WriteAllTextAsync(path, sb.ToString(), utf8, ct);
    }

    public async Task EnsureDeveloperEnvAsync(DnnProject project, DatabaseConfig db, CancellationToken ct)
    {
        if (File.Exists(project.DeveloperEnvFile)) return;
        var values = new Dictionary<string, string>
        {
            ["DOCKER_CONTAINER"] = _opts.Docker.ContainerName,
            ["DB_SERVER"] = $"localhost,{db.Port}",
            ["DB_NAME"] = db.DatabaseName,
            ["DB_USER"] = db.User,
            ["DB_PASSWORD"] = db.Password,
            ["BACKUP_DIR"] = project.BackupDirectory
        };
        await WriteAsync(project.DeveloperEnvFile, values, $"Developer env for {project.Name}", ct);
    }

    public async Task EnsureProductionEnvAsync(DnnProject project, CancellationToken ct)
    {
        if (File.Exists(project.ProductionEnvFile)) return;
        var values = new Dictionary<string, string>
        {
            ["DB_SERVER"] = "your-prod-sql-server,1433",
            ["DB_NAME"] = $"{project.Name}_prod",
            ["DB_USER"] = "",
            ["DB_PASSWORD"] = "",
            ["BACKUP_DIR"] = project.BackupDirectory,
            ["REMOTE_BACKUP_DIR"] = @"\\your-prod-sql-server\sqlbackups"
        };
        await WriteAsync(project.ProductionEnvFile, values, $"Production env for {project.Name} (edit me)", ct);
    }
}
