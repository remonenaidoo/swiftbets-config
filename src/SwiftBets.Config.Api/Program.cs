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
    // Staff need config.write; Steward's approved remediations (the kill switch) arrive as a service and are recorded under its client id.
    .AddPolicy(ConfigEndpoints.WritePermission, p => p.RequireAssertion(c => c.User.HasClaim("perm", ConfigEndpoints.WritePermission) || c.User.IsInRole("Service")));
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
