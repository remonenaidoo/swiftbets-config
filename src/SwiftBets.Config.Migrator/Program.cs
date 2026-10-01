using Microsoft.Extensions.Configuration;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;

var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
var connectionString = configuration["ConnectionStrings:SbConfig"];
if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync("ConnectionStrings:SbConfig is required.");
    return 2;
}

var source = new MigrationSource(typeof(Program).Assembly, 1);
if (configuration["Migrator:RollbackTo"] is { Length: > 0 } target)
{
    var rollback = MigrationRollback.Postgres(connectionString, source, int.Parse(target, System.Globalization.CultureInfo.InvariantCulture));
    await Console.Out.WriteLineAsync(rollback.Successful ? $"rolled back {rollback.RolledBack.Count} migrations" : rollback.Error);
    return rollback.Successful ? 0 : 1;
}

var result = MigrationRunner.RunPostgres(connectionString, configuration.GetValue("Migrator:EnsureDatabase", false), OutboxRegistration.PostgresMigrations, source);
if (!result.Successful)
{
    await Console.Error.WriteLineAsync(result.Error.ToString());
    return 1;
}

return 0;

public partial class Program;
