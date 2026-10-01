using SwiftBets.Config.Domain;
using SwiftBets.Contracts.Results;

namespace SwiftBets.Config.Application.Ports;

/// <summary>Settings in the database. A change commits with its history row, its event and its audit entry.</summary>
public interface IConfigStore
{
    Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken);

    Task<Setting?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Most recent first.</summary>
    Task<IReadOnlyList<Setting>> HistoryAsync(string key, int limit, CancellationToken cancellationToken);

    /// <summary>Locks the key, applies the change to what is stored, and saves and publishes it when it changed something.</summary>
    Task<Result<Setting>> ChangeAsync(string key, Func<Setting?, Result<SettingChange>> change, CancellationToken cancellationToken);

    /// <summary>Publishes every setting again, for a topic that lost its data; returns how many.</summary>
    Task<int> RepublishAsync(string actor, CancellationToken cancellationToken);
}
