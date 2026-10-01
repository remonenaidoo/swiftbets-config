# swiftbets-config

Operational settings for SwiftBets: the placement kill switch, placement mode, live bet delays, stake and payout limits, and feature flags.

- Operators change a setting through the console (`PUT /admin/config/{key}` with a value and a reason, permission `config.write`). Every change is validated against `ConfigKeys` in the contracts, versioned, kept in history and audited.
- Each change is published on the compacted topic `config.entries.v1`, keyed by setting, in the same transaction as the write (Postgres outbox). Services read it with `AddCompactedState<ConfigEntryV1>` and treat a missing key as their built-in default.
- `POST /admin/config/republish` puts every setting on the topic again, for a topic that lost its data.

| Project | Purpose |
|---|---|
| `SwiftBets.Config.Domain` | `Setting` and the change rule (reason required, validated value, unchanged value publishes nothing) |
| `SwiftBets.Config.Application` | `ConfigHandler` and the store port |
| `SwiftBets.Config.Infrastructure` | Postgres store (Dapper), outbox and audit |
| `SwiftBets.Config.Api` | Admin endpoints, `config.read` and `config.write` |
| `SwiftBets.Config.Migrator` | `sb_config` migrations with rollbacks, plus the outbox schema |

## Run the tests

```bash
../swiftbets-platform/scripts/fetch-shared-packages.sh .   # or pack-local.sh for unreleased contracts
dotnet test
```

Integration tests start Postgres and Redpanda with Testcontainers.
