using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Config.Api;
using SwiftBets.Config.Application;
using SwiftBets.Config.Infrastructure;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-config");
builder.Services.AddSwiftBetsWeb();
builder.Services.AddSwiftBetsJwtBearer(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ConfigEndpoints.ReadPermission, p => p.RequireClaim("perm", ConfigEndpoints.ReadPermission))
    .AddPolicy(ConfigEndpoints.WritePermission, p => p.RequireClaim("perm", ConfigEndpoints.WritePermission));
builder.Services.AddConfigApplication();
builder.Services.AddConfigInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.UseAuthentication();
app.UseAuthorization();
app.MapSwiftBetsOperationalEndpoints();
app.MapConfigEndpoints();

await app.RunAsync();
return 0;

public partial class Program;
