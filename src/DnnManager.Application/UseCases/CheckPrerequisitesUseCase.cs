using DnnManager.Application.Abstractions;
using DnnManager.Domain;
using Microsoft.Extensions.Logging;

namespace DnnManager.Application.UseCases;

public sealed class CheckPrerequisitesUseCase
{
    private readonly IPrerequisiteChecker _prereq;
    private readonly IUserPrompt _prompt;
    private readonly ILogger<CheckPrerequisitesUseCase> _log;

    public CheckPrerequisitesUseCase(IPrerequisiteChecker prereq, IUserPrompt prompt, ILogger<CheckPrerequisitesUseCase> log)
    {
        _prereq = prereq; _prompt = prompt; _log = log;
    }

    public async Task<Result> ExecuteAsync(IProgressReporter reporter, CancellationToken ct)
    {
        reporter.Step("Docker");
        var docker = await _prereq.CheckDockerAsync(reporter, ct);
        reporter.Step("IIS features");
        var iis = await _prereq.EnsureIisFeaturesAsync(reporter, _prompt, ct);
        return docker.Success && iis.Success ? Result.Ok() : Result.Fail("Some prerequisites are missing.");
    }
}
