using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Config.Application;
using SwiftBets.Contracts.Serialization;

namespace SwiftBets.Config.Api;

/// <summary>The console's settings view through the gateway's /api/admin/config routes.</summary>
public static class ConfigEndpoints
{
    public const string ReadPermission = "config.read";
    public const string WritePermission = "config.write";

    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/config");

        admin.MapGet("/", async (ConfigHandler config, CancellationToken cancellationToken) =>
            Results.Json(await config.ListAsync(cancellationToken), ContractJson.Options))
            .RequireAuthorization(ReadPermission);

        admin.MapGet("/{key}/history", async (string key, int? limit, ConfigHandler config, HttpContext context, CancellationToken cancellationToken) =>
            (await config.HistoryAsync(key, limit, cancellationToken)).ToHttpResult(context))
            .RequireAuthorization(ReadPermission);

        admin.MapPut("/{key}", async (string key, SetBody body, ConfigHandler config, HttpContext context, CancellationToken cancellationToken) =>
            (await config.SetAsync(key, body.Value ?? string.Empty, Actor(context), body.Reason, cancellationToken)).ToHttpResult(context))
            .RequireAuthorization(WritePermission);

        admin.MapPost("/republish", async (ConfigHandler config, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(new { republished = await config.RepublishAsync(Actor(context), cancellationToken) }))
            .RequireAuthorization(WritePermission);

        return endpoints;
    }

    private static string Actor(HttpContext context) => context.User.FindFirst("sub")?.Value ?? "operator";

    public sealed record SetBody(string? Value, string? Reason);
}
