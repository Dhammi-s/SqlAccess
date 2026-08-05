namespace SqlAccess.Api.Cicd.Services;

/// <summary>Long-running worker that drains the queue and runs each deployment in its own DI scope.</summary>
public sealed class DeploymentBackgroundService : BackgroundService
{
    private readonly IDeploymentQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeploymentBackgroundService> _log;

    public DeploymentBackgroundService(
        IDeploymentQueue queue, IServiceScopeFactory scopeFactory, ILogger<DeploymentBackgroundService> log)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int deploymentId;
            try
            {
                deploymentId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IDeploymentOrchestrator>();
                await orchestrator.RunAsync(deploymentId, stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Deployment {Id} crashed in the background worker.", deploymentId);
            }
        }
    }
}
