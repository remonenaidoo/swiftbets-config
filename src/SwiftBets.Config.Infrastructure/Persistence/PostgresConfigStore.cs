using Dapper;
using Npgsql;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Config.Application.Ports;
using SwiftBets.Config.Domain;
using SwiftBets.Contracts.Config;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Results;

namespace SwiftBets.Config.Infrastructure.Persistence;

public sealed class PostgresConfigStore(NpgsqlDataSource dataSource, IOutbox outbox, IAuditWriter audit, TimeProvider time) : IConfigStore
{
    private static readonly SqlResources Sql = SqlResources.For<PostgresConfigStore>();

    public async Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<Row>(new CommandDefinition(Sql.Get("Settings.List"), cancellationToken: cancellationToken))).Select(r => r.ToSetting())];
    }

    public async Task<Setting?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(Sql.Get("Settings.Get"), new { Key = key }, cancellationToken: cancellationToken)))?.ToSetting();
    }

    public async Task<IReadOnlyList<Setting>> HistoryAsync(string key, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. (await connection.QueryAsync<Row>(new CommandDefinition(Sql.Get("History.ForKey"), new { Key = key, Limit = limit }, cancellationToken: cancellationToken))).Select(r => r.ToSetting())];
    }

    public async Task<Result<Setting>> ChangeAsync(string key, Func<Setting?, Result<SettingChange>> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // A first write of a key has no row to lock; the advisory lock serialises it per key instead.
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Settings.Lock"), new { Key = key }, transaction, cancellationToken: cancellationToken));
        var current = (await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(Sql.Get("Settings.Get"), new { Key = key }, transaction, cancellationToken: cancellationToken)))?.ToSetting();

        var outcome = change(current);
        if (outcome.IsFailure || !outcome.Value.Changed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return outcome.IsFailure ? Result.Failure<Setting>(outcome.Error!) : Result.Success(outcome.Value.Setting);
        }

        var next = outcome.Value.Setting;
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Settings.Upsert"), next, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("History.Insert"), next, transaction, cancellationToken: cancellationToken));
        await PublishAsync(transaction, next, cancellationToken);
        await audit.RecordAsync(transaction, new AuditEntry(next.ChangedBy, "config.set", "setting", key, current, next), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(next);
    }

    public async Task<int> RepublishAsync(string actor, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var settings = (await connection.QueryAsync<Row>(new CommandDefinition(Sql.Get("Settings.List"), transaction: transaction, cancellationToken: cancellationToken))).Select(r => r.ToSetting()).ToList();
        foreach (var setting in settings)
        {
            await PublishAsync(transaction, setting, cancellationToken);
        }

        await audit.RecordAsync(transaction, new AuditEntry(actor, "config.republish", "settings", "all", null, new { settings.Count }), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return settings.Count;
    }

    private Task PublishAsync(NpgsqlTransaction transaction, Setting s, CancellationToken cancellationToken) =>
        outbox.EnqueueAsync(transaction, Topics.ConfigEntries, s.Key,
            EventEnvelope<ConfigEntryV1>.Create(new ConfigEntryV1(s.Key, s.Value, s.Version, s.ChangedBy, s.Reason, s.ChangedAt), time.GetUtcNow(), CorrelationContext.CorrelationId ?? CorrelationContext.NewId()),
            cancellationToken);

    // Npgsql reads timestamptz as a UTC DateTime.
    private sealed record Row(string Key, string Value, long Version, string ChangedBy, string Reason, DateTime ChangedAt)
    {
        public Setting ToSetting() => new(Key, Value, Version, ChangedBy, Reason, new DateTimeOffset(DateTime.SpecifyKind(ChangedAt, DateTimeKind.Utc)));
    }
}
