using DnnManager.Domain;

namespace DnnManager.Application.Abstractions;

/// <summary>Reports progress / status from use cases back to the presentation layer.</summary>
public interface IProgressReporter
{
    void Step(string title);
    void Info(string message);
    void Success(string message);
    void Fail(string message);
    /// <summary>Updates a single status line in place (e.g. a running download percentage).</summary>
    void Progress(string message);
}

public interface IUserPrompt
{
    Task<bool> ConfirmAsync(string question, bool defaultYes = false, CancellationToken ct = default);
}

public interface IProjectRepository
{
    IReadOnlyList<string> ListConfiguredProjects();
    IReadOnlyList<string> ListAllProjectDirectories();
    DnnProject Build(string projectName);
    bool ProjectExists(string projectName);
}

public interface IDnnReleaseService
{
    Task<Result<DnnRelease>> GetReleaseAsync(string apiUrl, string? version, CancellationToken ct);
    IReadOnlyList<string> KnownReleaseApis { get; }
}

public interface IDnnPackageInstaller
{
    Task<Result> DownloadAndExtractAsync(DnnRelease release, string projectDirectory, IProgressReporter reporter, CancellationToken ct);
}

public interface IIisManager
{
    Result CreateSite(string siteName, string physicalPath, string hostname, int port);
    Result RemoveSite(string siteName);
    Result StartSite(string siteName);
    bool SiteExists(string siteName);
    string? GetSiteState(string siteName);
    Result GrantPermissions(string path, IEnumerable<string> identities);
}

public interface IPrerequisiteChecker
{
    Task<Result> CheckDockerAsync(IProgressReporter reporter, CancellationToken ct);
    Task<Result> EnsureIisFeaturesAsync(IProgressReporter reporter, IUserPrompt prompt, CancellationToken ct);
}

public interface IDockerService
{
    Task<bool> IsContainerRunningAsync(string containerName, CancellationToken ct);
    Task<bool> DoesContainerExistAsync(string containerName, CancellationToken ct);
    Task<int?> GetPublishedPortAsync(string containerName, CancellationToken ct);
    Task<Result> StartContainerAsync(string containerName, CancellationToken ct);
    Task<Result> ComposeUpAsync(string composeFile, string envFile, string projectName, CancellationToken ct);
    Task<Result> ComposeDownAsync(string composeFile, string envFile, bool removeVolumes, CancellationToken ct);
    Task<Result<string>> ExecAsync(string containerName, IReadOnlyList<string> args, CancellationToken ct);
}

public interface IDockerConfigWriter
{
    Task WriteAsync(DnnProject project, int sqlPort, CancellationToken ct);
}

public interface ISqlServerService
{
    Task<Result> WaitReadyAsync(int timeoutSeconds, IProgressReporter reporter, CancellationToken ct);
    Task<Result<bool>> DatabaseExistsAsync(string database, CancellationToken ct);
    Task<Result> CreateDatabaseAndUserAsync(DatabaseConfig db, CancellationToken ct);
    Task<Result> DropDatabaseAndUserAsync(DatabaseConfig db, CancellationToken ct);
    Task<Result<string>> BackupDatabaseLocalAsync(string database, string backupFileName, CancellationToken ct);
    Task<Result> RestoreDatabaseLocalAsync(DatabaseConfig db, string backupFilePath, CancellationToken ct);

    /// <summary>
    /// Rewrites every PortalAlias whose HTTPAlias ends with <paramref name="hostnameSuffix"/> so that
    /// portal 0's primary alias becomes <paramref name="newHostname"/>. Used after cloning so the new
    /// site responds at its own host header instead of the source's.
    /// </summary>
    Task<Result> RemapPortalAliasesAsync(string database, string hostnameSuffix, string newHostname, CancellationToken ct);
}

public interface IEnvFileService
{
    IReadOnlyDictionary<string, string> Read(string path);
    Task WriteAsync(string path, IReadOnlyDictionary<string, string> values, string? header, CancellationToken ct);
    Task EnsureDeveloperEnvAsync(DnnProject project, DatabaseConfig db, CancellationToken ct);
    Task EnsureProductionEnvAsync(DnnProject project, CancellationToken ct);
}

public interface IHttpConnectivityChecker
{
    Task<Result<int>> CheckAsync(string url, int timeoutSeconds, CancellationToken ct);
}

public enum CloneSourceKind { LocalFolder, Ftp }

public sealed record CloneSource(
    CloneSourceKind Kind,
    string? LocalPath,
    string? FtpHost,
    int FtpPort,
    string? FtpUser,
    string? FtpPassword,
    string? FtpRemotePath);

public interface IProjectFileCopier
{
    /// <summary>Copies a DNN project's website files from the given source into <paramref name="destinationDirectory"/>.</summary>
    Task<Result> CopyAsync(CloneSource source, string destinationDirectory, IProgressReporter reporter, CancellationToken ct);
}

public interface IFtpBrowser
{
    /// <summary>Connects to the FTP server and returns immediate subdirectories of <paramref name="remotePath"/>.</summary>
    Task<Result<IReadOnlyList<string>>> ListDirectoriesAsync(
        string host, int port, string user, string password, string remotePath, CancellationToken ct);
}

public sealed record FtpProfile(string Name, string Host, int Port, string User, string EncryptedPassword, string RemotePath = "/");

/// <summary>Stores a single FTP connection per project (keyed by project name).</summary>
public interface IFtpProfileStore
{
    FtpProfile? Get(string project);
    void Save(string project, FtpProfile profile);
    /// <summary>Project names that have a saved FTP connection.</summary>
    IReadOnlyList<string> ListProjects();
    string Protect(string plain);
    string Unprotect(string encrypted);
}

public sealed record SqlProfile(string Name, string Server, string Database, string User, string EncryptedPassword);

/// <summary>Stores a single SQL connection per project (keyed by project name).</summary>
public interface ISqlProfileStore
{
    SqlProfile? Get(string project);
    void Save(string project, SqlProfile profile);
    /// <summary>Project names that have a saved SQL connection.</summary>
    IReadOnlyList<string> ListProjects();
    string Protect(string plain);
    string Unprotect(string encrypted);
}

public sealed record SiteSqlConnection(string Server, string Database, string User, string Password);

public interface IWebConfigService
{
    /// <summary>Reads the SiteSqlServer connection string from the project's web.config.</summary>
    Result<SiteSqlConnection> ReadSiteSqlServer(string webConfigPath);

    /// <summary>
    /// Rewrites both connectionStrings/add[@name='SiteSqlServer'] and
    /// appSettings/add[@key='SiteSqlServer'] to point at a new database.
    /// </summary>
    Result WriteSiteSqlServer(string webConfigPath, SiteSqlConnection newConnection);

    /// <summary>
    /// Removes the IIS URL Rewrite section (system.webServer/rewrite). Those rules are
    /// production-only (HTTPS redirects, request blocking) and require the URL Rewrite module,
    /// which is usually absent locally — otherwise IIS returns HTTP 500.19. Safe no-op if absent.
    /// </summary>
    Result RemoveRewriteRules(string webConfigPath);
}

/// <summary>
/// Exports a (possibly Azure) SQL database to a <c>.bacpac</c> and imports it into a local SQL Server,
/// using Microsoft's SqlPackage tool. Used to clone Azure SQL sources, which do not support BACKUP DATABASE.
/// </summary>
public interface IBacpacService
{
    /// <summary>True when the SqlPackage tool can be located on this machine.</summary>
    bool IsAvailable();

    /// <summary>Install hint shown when SqlPackage is missing.</summary>
    string InstallHint { get; }

    /// <summary>Exports <paramref name="source"/> to <paramref name="bacpacPath"/> on this host.</summary>
    Task<Result> ExportAsync(SiteSqlConnection source, string bacpacPath, IProgressReporter reporter, CancellationToken ct);

    /// <summary>Imports a <c>.bacpac</c> into a local SQL Server, creating <paramref name="databaseName"/>.</summary>
    Task<Result> ImportAsync(string targetServer, string saUser, string saPassword,
        string databaseName, string bacpacPath, IProgressReporter reporter, CancellationToken ct);
}

public interface IRemoteSqlBackupService
{
    /// <summary>
    /// Issues BACKUP DATABASE against the given (possibly external) SQL Server using SQL auth.
    /// Returns the host-side path of the resulting <c>.bak</c> when it can be read by this process.
    /// The caller supplies <paramref name="backupServerPath"/> \u2014 a path the SQL Server service can write to.
    /// For sources whose Data Source resolves to this machine that's a normal local path; for remote
    /// servers it must be a UNC share readable from here.
    /// </summary>
    Task<Result<string>> BackupAsync(SiteSqlConnection source, string backupServerPath, IProgressReporter reporter, CancellationToken ct);
}
