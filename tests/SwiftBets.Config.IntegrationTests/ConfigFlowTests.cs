extern alias migrator;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Contracts.Config;
using SwiftBets.Contracts.Messaging;

namespace SwiftBets.Config.IntegrationTests;

public sealed class ConfigFlowTests(PostgresFixture postgres, RedpandaFixture redpanda)
{
    [Fact]
    public async Task A_change_reaches_a_compacted_reader_once_and_an_unchanged_value_publishes_nothing()
    {
        await using var host = await ConfigHost.StartAsync(postgres, redpanda);
        using var client = Client(host, "config.read", "config.write");

        (await SetAsync(client, ConfigKeys.PlacementKillSwitch, "on", "feed outage")).GetProperty("version").GetInt64().ShouldBe(1);
        (await SetAsync(client, ConfigKeys.PlacementKillSwitch, "on", "still out")).GetProperty("version").GetInt64().ShouldBe(1);
        (await SetAsync(client, ConfigKeys.PlacementKillSwitch, "off", "feed back")).GetProperty("version").GetInt64().ShouldBe(2);

        using var reader = new CompactedStateConsumer<ConfigEntryV1>(Topics.ConfigEntries, Options.Create(new KafkaOptions
        {
            BootstrapServers = host.Bootstrap,
            Environment = host.Environment,
            ClientId = "config-reader",
        }), NullLogger<CompactedStateConsumer<ConfigEntryV1>>.Instance);
        await reader.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => reader.TryGet(ConfigKeys.PlacementKillSwitch, out var entry) && entry.Version == 2);
        reader.TryGet(ConfigKeys.PlacementKillSwitch, out var latest).ShouldBeTrue();
        latest.Value.ShouldBe("off");
        latest.Reason.ShouldBe("feed back");
        await reader.StopAsync(CancellationToken.None);

        (await OutboxCountAsync(host, Topics.ConfigEntries)).ShouldBe(2);
        var history = await client.GetFromJsonAsync<JsonElement>($"/admin/config/{ConfigKeys.PlacementKillSwitch}/history", TestContext.Current.CancellationToken);
        history.EnumerateArray().Select(h => h.GetProperty("value").GetString()).ShouldBe(["off", "on"]);
        var all = await client.GetFromJsonAsync<JsonElement>("/admin/config/", TestContext.Current.CancellationToken);
        all.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Republishing_puts_every_setting_on_the_topic_again_and_is_audited()
    {
        await using var host = await ConfigHost.StartAsync(postgres, redpanda);
        using var client = Client(host, "config.read", "config.write");
        await SetAsync(client, ConfigKeys.PlacementMode, "preMatchOnly", "trading review");
        await SetAsync(client, ConfigKeys.MaxStake("ZAR"), "5000000", "launch");

        using var response = await client.PostAsync(new Uri("/admin/config/republish", UriKind.Relative), null, TestContext.Current.CancellationToken);

        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("republished").GetInt32().ShouldBe(2);
        (await OutboxCountAsync(host, Topics.ConfigEntries)).ShouldBe(4);
        (await OutboxCountAsync(host, Topics.AuditRecorded)).ShouldBe(3);
    }

    [Fact]
    public async Task Bad_values_missing_reasons_and_missing_permissions_are_refused()
    {
        await using var host = await ConfigHost.StartAsync(postgres, redpanda);
        using var writer = Client(host, "config.read", "config.write");
        using var reader = Client(host, "config.read");

        (await PutAsync(writer, ConfigKeys.PlacementMode, "paused", "x")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(writer, ConfigKeys.PlacementKillSwitch, "on", "")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(reader, ConfigKeys.PlacementKillSwitch, "on", "feed outage")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var anonymous = host.CreateClient();
        (await anonymous.GetAsync(new Uri("/admin/config/", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await writer.GetAsync(new Uri("/admin/config/flags.nothing/history", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OutboxCountAsync(host, Topics.ConfigEntries)).ShouldBe(0);
    }

    [Fact]
    public async Task Steward_engages_the_kill_switch_as_a_service_and_the_history_names_it()
    {
        await using var host = await ConfigHost.StartAsync(postgres, redpanda);
        using var steward = host.CreateClient();
        steward.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ConfigHost.ServiceToken("steward"));
        using var reader = Client(host, "config.read");

        (await PutAsync(steward, ConfigKeys.PlacementKillSwitch, "on", "Steward: approved remediation")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var history = await reader.GetFromJsonAsync<JsonElement>($"/admin/config/{ConfigKeys.PlacementKillSwitch}/history", TestContext.Current.CancellationToken);
        history[0].GetProperty("changedBy").GetString().ShouldBe("client:steward");
    }

    [Fact]
    public async Task Migrator_is_idempotent_rolls_back_and_needs_a_connection_string()
    {
        var name = "cfgm_" + Guid.NewGuid().ToString("N")[..10];
        await using (var server = new NpgsqlConnection(postgres.ConnectionString))
        {
            await server.ExecuteAsync($"CREATE DATABASE {name}");
        }

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = name }.ConnectionString;
        (await ConfigHost.MigrateAsync(connectionString)).ShouldBe(0);
        (await ConfigHost.MigrateAsync(connectionString)).ShouldBe(0);
        (await ConfigHost.MigrateAsync(connectionString, "--Migrator:RollbackTo=0")).ShouldBe(0);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            (await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('config.settings') IS NULL")).ShouldBeTrue();
        }

        (await ConfigHost.MigrateAsync(connectionString)).ShouldBe(0);
        var entryPoint = typeof(migrator::Program).Assembly.EntryPoint!;
        var result = entryPoint.Invoke(null, [Array.Empty<string>()]);
        (result is Task<int> task ? await task : (int)result!).ShouldBe(2);
    }

    private static HttpClient Client(ConfigHost host, params string[] permissions)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ConfigHost.StaffToken(permissions));
        return client;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string key, string value, string reason) =>
        client.PutAsJsonAsync(new Uri($"/admin/config/{key}", UriKind.Relative), new { value, reason }, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> SetAsync(HttpClient client, string key, string value, string reason)
    {
        using var response = await PutAsync(client, key, value, reason);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static async Task<int> OutboxCountAsync(ConfigHost host, string topicBase)
    {
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM outbox.messages WHERE topic = @Topic", new { Topic = TopicName.For(topicBase, host.Environment).Value });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 150 && !condition(); i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        condition().ShouldBeTrue();
    }
}
