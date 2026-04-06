using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public sealed class AIModelInstallCoordinator : IAIModelInstallCoordinator
{
    private readonly IAIModelsSetupService _setup;
    private readonly object _gate = new();
    private Task<Result<AIModelsSetupResult>>? _activeTask;
    private CancellationTokenSource? _cts;

    public AIModelInstallCoordinator(IAIModelsSetupService setup)
    {
        _setup = setup;
    }

    public bool IsRunning { get; private set; }

    public event Action<AIModelsSetupProgress>? ProgressChanged;
    public event Action<Result<AIModelsSetupResult>>? Completed;

    public Task<Result<AIModelsSetupResult>> StartDownloadAsync(
        IReadOnlySet<string>? optionalAdditionalComponents,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_activeTask != null && !_activeTask.IsCompleted)
                return _activeTask;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _cts.Token;
            _activeTask = RunAsync(optionalAdditionalComponents, token);
            return _activeTask;
        }
    }

    private async Task<Result<AIModelsSetupResult>> RunAsync(
        IReadOnlySet<string>? optionalAdditionalComponents,
        CancellationToken cancellationToken)
    {
        IsRunning = true;
        try
        {
            var progress = new Progress<AIModelsSetupProgress>(p => ProgressChanged?.Invoke(p));
            var result = await _setup.DownloadAndExtractMissingAsync(progress, optionalAdditionalComponents, cancellationToken).ConfigureAwait(false);
            Completed?.Invoke(result);
            return result;
        }
        finally
        {
            IsRunning = false;
        }
    }
}
