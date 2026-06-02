using DnnManager.Application.Abstractions;
using DnnManager.Application.Configuration;
using DnnManager.Domain;
using DnnManager.Infrastructure.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DnnManager.Infrastructure.Prereq;

public sealed class WindowsPrerequisiteChecker : IPrerequisiteChecker
{
    private readonly ProcessRunner _proc;
    private readonly AppOptions _opts;
    private readonly ILogger<WindowsPrerequisiteChecker> _log;

    public WindowsPrerequisiteChecker(ProcessRunner proc, IOptions<AppOptions> opts, ILogger<WindowsPrerequisiteChecker> log)
    {
        _proc = proc; _opts = opts.Value; _log = log;
    }

    public async Task<Result> CheckDockerAsync(IProgressReporter reporter, CancellationToken ct)
    {
        var v = await _proc.RunAsync("docker", new[] { "--version" }, ct);
        if (!v.Success) { reporter.Fail("Docker CLI not found or failed."); return Result.Fail("Docker missing"); }
        reporter.Success(v.StdOut.Trim());
        var info = await _proc.RunAsync("docker", new[] { "info" }, ct);
        if (!info.Success) { reporter.Fail("Docker daemon not running."); return Result.Fail("Docker daemon offline"); }
        reporter.Success("Docker daemon is running.");
        return Result.Ok();
    }

    public async Task<Result> EnsureIisFeaturesAsync(IProgressReporter reporter, IUserPrompt prompt, CancellationToken ct)
    {
        if (_opts.RequiredIisFeatures.Count == 0)
        {
            reporter.Info("No IIS features configured to check.");
            return Result.Ok();
        }
        var missing = new List<IisFeatureSetting>();
        var enabled = new List<IisFeatureSetting>();
        foreach (var f in _opts.RequiredIisFeatures)
        {
            var r = await _proc.RunAsync("powershell.exe", new[]
            {
                "-NoProfile","-Command",
                $"(Get-WindowsOptionalFeature -Online -FeatureName '{f.Name}' -ErrorAction SilentlyContinue).State"
            }, ct);
            if (r.Success && r.StdOut.Contains("Enabled", StringComparison.OrdinalIgnoreCase))
                enabled.Add(f);
            else
                missing.Add(f);
        }

        foreach (var f in enabled) reporter.Success($"{f.Label} ({f.Name})");
        if (missing.Count == 0) return Result.Ok();

        reporter.Info("Missing IIS features:");
        foreach (var f in missing) reporter.Fail($"{f.Label} ({f.Name})");
        if (!await prompt.ConfirmAsync("Enable them now?", true, ct))
            return Result.Fail("IIS features missing.");

        foreach (var f in missing)
        {
            reporter.Info($"Enabling {f.Label}…");
            var r = await _proc.RunAsync("powershell.exe", new[]
            {
                "-NoProfile","-Command",
                $"Enable-WindowsOptionalFeature -Online -FeatureName '{f.Name}' -All -NoRestart | Out-Null"
            }, ct);
            if (!r.Success) reporter.Fail($"Failed: {f.Label}");
        }
        reporter.Success("IIS feature changes applied (a reboot may be required).");
        return Result.Ok();
    }
}
