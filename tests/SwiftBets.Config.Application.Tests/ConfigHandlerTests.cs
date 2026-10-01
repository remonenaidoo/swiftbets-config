using Microsoft.Extensions.Time.Testing;
using SwiftBets.Config.Application.Ports;
using SwiftBets.Config.Domain;
using SwiftBets.Contracts.Config;
using SwiftBets.Contracts.Results;

namespace SwiftBets.Config.Application.Tests;

public sealed class ConfigHandlerTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeStore _store = new();

    private ConfigHandler Handler => new(_store, _time);

    [Fact]
    public async Task A_new_setting_starts_at_version_one_and_each_change_adds_one()
    {
        var first = await Handler.SetAsync(ConfigKeys.PlacementKillSwitch, "on", "ops-1", "feed outage", CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(5));
        var second = await Handler.SetAsync(ConfigKeys.PlacementKillSwitch, "off", "ops-2", " feed back ", CancellationToken.None);

        first.Value.Version.ShouldBe(1);
        second.Value.ShouldBe(new Setting(ConfigKeys.PlacementKillSwitch, "off", 2, "ops-2", "feed back", _time.GetUtcNow()));
        _store.Published.Select(s => s.Version).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Setting_the_value_it_already_has_publishes_nothing()
    {
        await Handler.SetAsync(ConfigKeys.PlacementMode, "closed", "ops-1", "maintenance", CancellationToken.None);

        var again = await Handler.SetAsync(ConfigKeys.PlacementMode, "closed", "ops-2", "maintenance", CancellationToken.None);

        again.Value.Version.ShouldBe(1);
        again.Value.ChangedBy.ShouldBe("ops-1");
        _store.Published.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("placement.mode", "paused", "feed outage", "invalid_value")]
    [InlineData("unknown.key", "x", "because", "invalid_value")]
    [InlineData("placement.kill-switch", "on", "  ", "reason_required")]
    [InlineData("placement.kill-switch", "on", null, "reason_required")]
    public async Task Bad_changes_are_refused_with_a_reason(string key, string value, string? reason, string code)
    {
        var outcome = await Handler.SetAsync(key, value, "ops-1", reason, CancellationToken.None);

        outcome.Error!.Code.ShouldBe(code);
        _store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_overlong_reason_is_refused()
    {
        var outcome = await Handler.SetAsync(ConfigKeys.PlacementKillSwitch, "on", "ops-1", new string('x', Setting.MaxReasonLength + 1), CancellationToken.None);

        outcome.Error!.Code.ShouldBe("reason_required");
    }

    [Fact]
    public async Task History_is_newest_first_and_a_key_never_set_is_not_found()
    {
        await Handler.SetAsync(ConfigKeys.MaxStake("ZAR"), "100000", "ops-1", "launch", CancellationToken.None);
        await Handler.SetAsync(ConfigKeys.MaxStake("ZAR"), "200000", "ops-1", "raise", CancellationToken.None);

        (await Handler.HistoryAsync(ConfigKeys.MaxStake("ZAR"), null, CancellationToken.None)).Value.Select(s => s.Value).ShouldBe(["200000", "100000"]);
        (await Handler.HistoryAsync(ConfigKeys.MaxStake("ZAR"), 1, CancellationToken.None)).Value.Count.ShouldBe(1);
        (await Handler.HistoryAsync(ConfigKeys.MaxStake("USD"), null, CancellationToken.None)).Error!.Code.ShouldBe("setting_not_found");
    }

    [Fact]
    public async Task List_and_republish_cover_every_setting()
    {
        await Handler.SetAsync(ConfigKeys.Flag("cashout"), "true", "ops-1", "launch", CancellationToken.None);
        await Handler.SetAsync(ConfigKeys.PlacementKillSwitch, "off", "ops-1", "launch", CancellationToken.None);

        (await Handler.ListAsync(CancellationToken.None)).Count.ShouldBe(2);
        (await Handler.RepublishAsync("ops-1", CancellationToken.None)).ShouldBe(2);
        _store.Published.Count.ShouldBe(4);
    }

    private sealed class FakeStore : IConfigStore
    {
        private readonly Dictionary<string, List<Setting>> _history = new(StringComparer.Ordinal);

        public List<Setting> Published { get; } = [];

        public Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Setting>>([.. _history.Values.Select(h => h[^1]).OrderBy(s => s.Key, StringComparer.Ordinal)]);

        public Task<Setting?> GetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_history.TryGetValue(key, out var h) ? h[^1] : null);

        public Task<IReadOnlyList<Setting>> HistoryAsync(string key, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Setting>>(_history.TryGetValue(key, out var h) ? [.. Enumerable.Reverse(h).Take(limit)] : []);

        public async Task<Result<Setting>> ChangeAsync(string key, Func<Setting?, Result<SettingChange>> change, CancellationToken cancellationToken)
        {
            var outcome = change(await GetAsync(key, cancellationToken));
            if (outcome.IsFailure)
            {
                return Result.Failure<Setting>(outcome.Error!);
            }

            if (outcome.Value.Changed)
            {
                (_history.TryGetValue(key, out var h) ? h : _history[key] = []).Add(outcome.Value.Setting);
                Published.Add(outcome.Value.Setting);
            }

            return Result.Success(outcome.Value.Setting);
        }

        public async Task<int> RepublishAsync(string actor, CancellationToken cancellationToken)
        {
            var all = await ListAsync(cancellationToken);
            Published.AddRange(all);
            return all.Count;
        }
    }
}
