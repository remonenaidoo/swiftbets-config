using SwiftBets.Config.Application.Ports;
using SwiftBets.Config.Domain;
using SwiftBets.Contracts.Results;

namespace SwiftBets.Config.Application;

public sealed class ConfigHandler(IConfigStore store, TimeProvider time)
{
    public const int MaxHistory = 100;

    public Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

    public async Task<Result<IReadOnlyList<Setting>>> HistoryAsync(string key, int? limit, CancellationToken cancellationToken)
    {
        var history = await store.HistoryAsync(key, Math.Clamp(limit ?? 20, 1, MaxHistory), cancellationToken);
        return history.Count == 0 ? Result.Failure<IReadOnlyList<Setting>>(ConfigErrors.NotFound) : Result.Success(history);
    }

    public Task<Result<Setting>> SetAsync(string key, string value, string actor, string? reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var now = time.GetUtcNow();
        return store.ChangeAsync(key, current => Setting.Change(current, key, value ?? string.Empty, actor, reason, now), cancellationToken);
    }

    public Task<int> RepublishAsync(string actor, CancellationToken cancellationToken) => store.RepublishAsync(actor, cancellationToken);
}
