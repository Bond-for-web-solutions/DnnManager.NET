namespace DnnManager.Domain;

public enum DnnEnvironment { Developer, Production }

public sealed record DnnProject(
    string Name,
    string ProjectDirectory,
    string DockerDirectory,
    string ComposeFile,
    string EnvFile,
    string DeveloperEnvFile,
    string ProductionEnvFile,
    string BackupDirectory);

public sealed record DnnRelease(string Version, string TagName, string DownloadUrl);

public sealed record DatabaseConfig(
    string Server,
    string DatabaseName,
    string User,
    string Password,
    string Collation,
    int Port,
    string BackupDirectory,
    string? RemoteBackupDirectory = null);

public sealed record ProjectStatus(
    string Name,
    string ProjectDirectory,
    bool IisSiteExists,
    string? IisSiteState,
    long DirectorySizeBytes,
    bool ContainerRunning,
    string? DatabaseName,
    string? DatabaseUser,
    int? SqlPort,
    string SiteUrl);

public sealed record DockerSettings(
    string ContainerName,
    string VolumeName,
    string SaPassword,
    int DefaultPort,
    string Collation,
    string MssqlPid);

public sealed record IisFeature(string Name, string Label);
