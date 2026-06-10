namespace DnnManager.Application.Configuration;

public sealed class AppOptions
{
    public const string SectionName = "DnnManager";

    public string BaseDirectory { get; set; } = @"C:\DNN";
    public int SitePort { get; set; } = 80;
    public string HostnameSuffix { get; set; } = "dnndev.me";
    public DockerOptions Docker { get; set; } = new();
    // NOTE: Intentionally empty. Microsoft.Extensions.Configuration *appends* to
    // collection defaults when binding, so any items listed here would be
    // duplicated by the matching entries in appsettings.json. Defaults live in
    // appsettings.json only.
    public IReadOnlyList<string> GitHubReleaseApis { get; set; } = Array.Empty<string>();
    public IReadOnlyList<IisFeatureSetting> RequiredIisFeatures { get; set; } = Array.Empty<IisFeatureSetting>();
    public ConsoleOptions Console { get; set; } = new();
}

public sealed class ConsoleOptions
{
    public int WindowWidth { get; set; } = 100;
    public int WindowHeight { get; set; } = 30;
}

public sealed class DockerOptions
{
    public string ContainerName { get; set; } = "dnn-sqlserver";
    public string VolumeName { get; set; } = "dnn_sqlserver_data";
    public string SaPassword { get; set; } = "Admin@123";
    public int DefaultPort { get; set; } = 1433;
    public string Collation { get; set; } = "Latin1_General_CI_AS";
    public string MssqlPid { get; set; } = "Developer";
    public string DefaultDbNameSuffix { get; set; } = "_dnndev";
}

public sealed class IisFeatureSetting
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
