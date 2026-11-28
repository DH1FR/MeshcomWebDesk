using System.Net;
using Microsoft.AspNetCore.Http.Extensions;
using MeshcomWebDesk.Services.Update;

namespace MeshcomWebDesk.Services.Access;

/// <summary>
/// Middleware and endpoints of the optional password protection: <c>/login</c> (a small standalone page without
/// Blazor), <c>/api/login</c>, <c>/logout</c> and <c>/api/access/ticket</c> (signs the browser in that just set a
/// password in Settings).
/// </summary>
public static class AccessEndpoints
{
    /// <summary>Reachable without the password: not sensitive, and the browser fetches them without cookies.</summary>
    private static bool IsOpenPath(PathString p) =>
        // Has its own X-Api-Key (Home Assistant & co. cannot log in with a cookie)
        p.StartsWithSegments("/api/telemetry")
        || p.StartsWithSegments("/icons")
        || p.Equals("/manifest.webmanifest")
        || p.Equals("/favicon.png");

    private static bool IsAccessPath(PathString p) =>
        p.Equals("/login") || p.Equals("/logout") || p.Equals("/api/login") || p.Equals("/api/access/ticket");

    private static bool HasProxyHeaders(HttpContext ctx) =>
        ctx.Request.Headers.ContainsKey("X-Forwarded-For")
        || ctx.Request.Headers.ContainsKey("Forwarded")
        || ctx.Request.Headers.ContainsKey("X-Real-IP")
        || ctx.Request.Headers.ContainsKey("X-Forwarded-Proto");

    /// <summary>Only a path on this site: no other host, no loop through the login pages.</summary>
    internal static string SafeReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/' || url.StartsWith("//") || url.StartsWith("/\\")
            || url.Any(char.IsControl)
            || url.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("/logout", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return "/";
        return url;
    }

    public static void UseAccessProtection(this WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            var access = ctx.RequestServices.GetRequiredService<AccessService>();
            if (!access.IsProtected(ctx) || IsOpenPath(ctx.Request.Path))
            {
                await next();
                return;
            }

            // The password must not cross the network in the clear when https is available: send a browser that
            // is not on this PC to the https address of WebDesk (not behind a proxy, which does that itself).
            if ((HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
                && !AccessService.IsHttps(ctx) && !HasProxyHeaders(ctx)
                && ctx.RequestServices.GetRequiredService<RemoteAccessService>().FindHttpsPort() is { } httpsPort)
            {
                var host = new HostString(ctx.Request.Host.Host, httpsPort);
                ctx.Response.Redirect(UriHelper.BuildAbsolute("https", host, ctx.Request.PathBase,
                    ctx.Request.Path, ctx.Request.QueryString), permanent: false);
                return;
            }

            if (IsAccessPath(ctx.Request.Path))
            {
                await next();
                return;
            }

            if (access.HasValidCookie(ctx))
            {
                access.RenewCookieIfNeeded(ctx);
                await next();
                return;
            }

            ctx.Response.Headers.CacheControl = "no-store";
            if (HttpMethods.IsGet(ctx.Request.Method)
                && ctx.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                var back = ctx.Request.Path + ctx.Request.QueryString;
                ctx.Response.Redirect("/login?returnUrl=" + Uri.EscapeDataString(back));
                return;
            }
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        });
    }

    public static void MapAccessEndpoints(this WebApplication app)
    {
        app.MapGet("/login", (HttpContext ctx, AccessService access, LanguageService loc) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            // Nothing to log in to: back to the app
            if (!access.IsProtected(ctx)) return Results.Redirect("/");
            if (access.HasValidCookie(ctx)) return Results.Redirect(SafeReturnUrl(ctx.Request.Query["returnUrl"]));

            var wait = int.TryParse(ctx.Request.Query["wait"], out var w) ? w : 0;
            return Results.Content(
                LoginPage.Render(loc, SafeReturnUrl(ctx.Request.Query["returnUrl"]),
                    ctx.Request.Query["e"] == "1", wait,
                    // On this PC (localhost) nothing crosses the network, so no warning there
                    insecure: !AccessService.IsHttps(ctx) && !UpdateEndpoints.IsLocalRequest(ctx)),
                "text/html; charset=utf-8");
        });

        app.MapPost("/api/login", async (HttpContext ctx, AccessService access) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var back = SafeReturnUrl(form["returnUrl"]);
            var key  = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            var res  = await access.TryLoginAsync(key, form["password"].ToString());
            if (res.Ok)
            {
                access.IssueCookie(ctx);
                return Results.Redirect(back);
            }
            var q = "/login?e=1" + (res.RetryAfterSeconds > 0 ? "&wait=" + res.RetryAfterSeconds : "")
                  + "&returnUrl=" + Uri.EscapeDataString(back);
            return Results.Redirect(q);
        }).DisableAntiforgery();

        app.MapGet("/logout", (HttpContext ctx, AccessService access) =>
        {
            access.ClearCookie(ctx);
            return Results.Redirect("/login");
        });

        // Settings page → this browser is signed in right after the password was set (a Blazor circuit cannot set cookies)
        app.MapGet("/api/access/ticket", (HttpContext ctx, AccessService access) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (access.ConsumeTicket(ctx.Request.Query["t"]))
                access.IssueCookie(ctx);
            return Results.Redirect(SafeReturnUrl(ctx.Request.Query["returnUrl"]));
        });
    }
}

/// <summary>The standalone login page (inline CSS, no scripts, no Blazor).</summary>
internal static class LoginPage
{
    public static string Render(LanguageService loc, string returnUrl, bool error, int wait, bool insecure)
    {
        string E(string s) => WebUtility.HtmlEncode(s);

        var message = "";
        if (wait > 0)
            message = $"<p class=\"msg err\">{E(loc.TF("Zu viele Versuche. Bitte in {0} s erneut versuchen.", "Too many attempts. Please try again in {0} s.", wait))}</p>";
        else if (error)
            message = $"<p class=\"msg err\">{E(loc.T("Passwort falsch.", "Wrong password."))}</p>";

        var warning = insecure
            ? $"<p class=\"msg warn\">⚠️ {E(loc.T("Diese Verbindung ist nicht verschlüsselt (http): das Passwort wird im Klartext übertragen.", "This connection is not encrypted (http): the password is sent in clear text."))}</p>"
            : "";

        return $$"""
<!DOCTYPE html>
<html lang="{{E(loc.Current)}}" translate="no">
<head>
<meta charset="utf-8">
<meta name="google" content="notranslate">
<meta name="viewport" content="width=device-width, initial-scale=1.0, viewport-fit=cover">
<meta name="theme-color" content="#0f3460">
<meta name="robots" content="noindex">
<title>MeshCom WebDesk</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; }
  body { margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
         background: #0d1117; color: #e6edf3; font: 16px/1.4 system-ui, -apple-system, "Segoe UI", sans-serif;
         padding: max(16px, env(safe-area-inset-top)) 16px 16px; }
  .card { width: 100%; max-width: 380px; background: #161b22; border: 1px solid #30363d; border-radius: 12px; padding: 28px 24px; }
  h1 { font-size: 1.25rem; margin: 0 0 4px; }
  .sub { color: #8b949e; margin: 0 0 20px; font-size: .9rem; }
  label { display: block; font-size: .85rem; color: #8b949e; margin-bottom: 6px; }
  input[type=password] { width: 100%; padding: 12px; font-size: 1rem; color: inherit; background: #0d1117;
         border: 1px solid #30363d; border-radius: 8px; }
  input[type=password]:focus { outline: 2px solid #1f6feb; border-color: transparent; }
  button { width: 100%; margin-top: 16px; padding: 12px; font-size: 1rem; font-weight: 600; color: #fff;
         background: #1f6feb; border: 0; border-radius: 8px; cursor: pointer; }
  button:hover { background: #388bfd; }
  .msg { margin: 14px 0 0; padding: 10px 12px; border-radius: 8px; font-size: .88rem; }
  .err  { background: #2b0d0d; border: 1px solid #6e2020; color: #f0a0a0; }
  .warn { background: #2d2308; border: 1px solid #6b5316; color: #e3c05a; }
</style>
</head>
<body>
<form class="card" method="post" action="/api/login">
  <h1>🔒 MeshCom WebDesk</h1>
  <p class="sub">{{E(loc.T("Bitte Zugangspasswort eingeben.", "Please enter the access password."))}}</p>
  <label for="pw">{{E(loc.T("Passwort", "Password"))}}</label>
  <input id="pw" name="password" type="password" autocomplete="current-password" autofocus required>
  <input type="hidden" name="returnUrl" value="{{E(returnUrl)}}">
  <button type="submit">{{E(loc.T("Anmelden", "Log in"))}}</button>
  {{message}}
  {{warning}}
</form>
</body>
</html>
""";
    }
}
