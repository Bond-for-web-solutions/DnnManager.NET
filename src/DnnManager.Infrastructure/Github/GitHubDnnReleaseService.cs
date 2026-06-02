using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DnnManager.Application.Abstractions;
using DnnManager.Application.Configuration;
using DnnManager.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DnnManager.Infrastructure.Github;

public sealed class GitHubDnnReleaseService : IDnnReleaseService
{
    private readonly HttpClient _http;
    private readonly AppOptions _opts;
    private readonly ILogger<GitHubDnnReleaseService> _log;

    public GitHubDnnReleaseService(HttpClient http, IOptions<AppOptions> opts, ILogger<GitHubDnnReleaseService> log)
    {
        _http = http; _opts = opts.Value; _log = log;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.Add("User-Agent", "DnnManager-NET");
    }

    public IReadOnlyList<string> KnownReleaseApis => _opts.GitHubReleaseApis;

    public async Task<Result<DnnRelease>> GetReleaseAsync(string apiUrl, string? version, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                var releases = await _http.GetFromJsonAsync<List<GhRelease>>(apiUrl, ct);
                if (releases is null) return Result<DnnRelease>.Fail("Empty release list.");
                foreach (var r in releases.Where(r => !r.Prerelease && !r.Draft))
                {
                    var asset = FindAsset(r);
                    if (asset != null)
                        return Result<DnnRelease>.Ok(new DnnRelease(r.TagName.TrimStart('v'), r.TagName, asset.BrowserDownloadUrl));
                }
                return Result<DnnRelease>.Fail("No suitable DNN release found.");
            }
            else
            {
                var rel = await _http.GetFromJsonAsync<GhRelease>($"{apiUrl}/tags/v{version}", ct);
                if (rel is null) return Result<DnnRelease>.Fail("Release not found.");
                var asset = FindAsset(rel);
                if (asset is null) return Result<DnnRelease>.Fail("Release has no DNN Install ZIP asset.");
                return Result<DnnRelease>.Ok(new DnnRelease(version, rel.TagName, asset.BrowserDownloadUrl));
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GitHub release lookup failed");
            return Result<DnnRelease>.Fail(ex.Message);
        }
    }

    private static GhAsset? FindAsset(GhRelease r)
        => r.Assets.FirstOrDefault(a => System.Text.RegularExpressions.Regex.IsMatch(a.Name, "DNN_Platform.*Install\\.zip$"));

    private sealed class GhRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("assets")] public List<GhAsset> Assets { get; set; } = new();
    }
    private sealed class GhAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
    }
}

public sealed class DnnPackageInstaller : IDnnPackageInstaller
{
    private readonly HttpClient _http;
    private readonly ILogger<DnnPackageInstaller> _log;

    public DnnPackageInstaller(HttpClient http, ILogger<DnnPackageInstaller> log)
    {
        _http = http; _log = log;
    }

    public async Task<Result> DownloadAndExtractAsync(DnnRelease release, string projectDirectory,
        IProgressReporter reporter, CancellationToken ct)
    {
        try
        {
            var zipPath = Path.Combine(projectDirectory, $"DNN_Platform_{release.Version}_Install.zip");
            reporter.Info($"Downloading DNN {release.Version}…");

            using (var resp = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(zipPath);
                await resp.Content.CopyToAsync(fs, ct);
            }
            reporter.Success($"Downloaded: {zipPath}");

            reporter.Info("Extracting…");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, projectDirectory, overwriteFiles: true);
            File.Delete(zipPath);
            reporter.Success("Extraction complete.");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DNN install failed");
            return Result.Fail(ex.Message);
        }
    }
}

public sealed class HttpConnectivityChecker : IHttpConnectivityChecker
{
    public async Task<Result<int>> CheckAsync(string url, int timeoutSeconds, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            using var resp = await http.GetAsync(url, ct);
            return Result<int>.Ok((int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            return Result<int>.Fail(ex.Message);
        }
    }
}
