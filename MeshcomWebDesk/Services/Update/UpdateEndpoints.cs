using System.Net;

namespace MeshcomWebDesk.Services.Update;

/// <summary>
/// HTTP endpoints used by the update page. Starting an update or a rollback is only allowed for requests
/// coming from this machine (loopback, no proxy headers) and carrying the custom header, which a foreign
/// web page cannot add to a cross-origin request.
/// </summary>
public static class UpdateEndpoints
{
    public const string RequiredHeader = "X-MeshcomWebDesk";

    public static bool IsLocalRequest(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null || !IPAddress.IsLoopback(ip)) return false;
        // Behind a reverse proxy every request looks local – refuse when proxy headers are present.
        return !ctx.Request.Headers.ContainsKey("X-Forwarded-For")
            && !ctx.Request.Headers.ContainsKey("Forwarded")
            && !ctx.Request.Headers.ContainsKey("X-Real-IP");
    }

    public static void MapUpdateEndpoints(this WebApplication app)
    {
        app.MapGet("/api/update/local", (HttpContext ctx) => Results.Json(new { local = IsLocalRequest(ctx) }));

        app.MapPost("/api/update/start", async (HttpContext ctx, SelfUpdateService update) =>
        {
            if (!IsLocalRequest(ctx) || !ctx.Request.Headers.ContainsKey(RequiredHeader))
                return Results.Json(new { ok = false, message = "forbidden" }, statusCode: 403);
            var (ok, message) = await update.StartUpdateAsync(CancellationToken.None);
            return Results.Json(new { ok, message });
        }).DisableAntiforgery();

        app.MapPost("/api/update/rollback", async (HttpContext ctx, SelfUpdateService update, string name) =>
        {
            if (!IsLocalRequest(ctx) || !ctx.Request.Headers.ContainsKey(RequiredHeader))
                return Results.Json(new { ok = false, message = "forbidden" }, statusCode: 403);
            var (ok, message) = await update.RollbackAsync(name);
            return Results.Json(new { ok, message });
        }).DisableAntiforgery();
    }
}
