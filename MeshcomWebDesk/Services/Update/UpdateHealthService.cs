namespace MeshcomWebDesk.Services.Update;

/// <summary>
/// Confirms a freshly installed version: once the app has run for a minute after the update, the
/// probation marker is removed (otherwise <see cref="UpdateRecovery.OnStartup"/> rolls back on repeated crashes).
/// </summary>
public sealed class UpdateHealthService : BackgroundService
{
    private static readonly TimeSpan HealthyAfter = TimeSpan.FromSeconds(60);

    private readonly SelfUpdateService _update;
    private readonly ILogger<UpdateHealthService> _logger;

    public UpdateHealthService(SelfUpdateService update, ILogger<UpdateHealthService> logger)
    {
        _update = update;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dataPath = _update.DataPathPublic;
        var state = UpdateRecovery.ReadState(dataPath);
        if (state is null) return;

        try { await Task.Delay(HealthyAfter, stoppingToken); }
        catch (OperationCanceledException) { return; }

        UpdateRecovery.ClearState(dataPath);
        var cleaned = FileSwap.CleanupTemp(_update.Install.AppDir);
        UpdateRecovery.WriteResult(dataPath, new UpdateResult
        {
            Kind = "ok", From = state.From, To = state.To, WhenUtc = DateTime.UtcNow
        });
        _logger.LogInformation("Update to v{To} confirmed (healthy for {Seconds}s); backup v{From} kept for rollback, {Cleaned} temp file(s) removed.",
            state.To, (int)HealthyAfter.TotalSeconds, state.From, cleaned);
    }
}
