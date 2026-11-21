using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>Why a net console operation did not work.</summary>
public enum NodeConsoleError
{
    None,
    /// <summary>The TCP connection could not be opened (net console off, wrong IP, firewall).</summary>
    Unreachable,
    /// <summary>The node refused the HMAC login – wrong node password.</summary>
    AuthFailed,
    /// <summary>The shared console is already connected to another host; WebDesk does not hijack it.</summary>
    Busy,
    /// <summary>Anything else (connection dropped, unexpected answer).</summary>
    Other,
}

/// <summary>Values read from the node's console. A value is null when the node did not report it.</summary>
public sealed record NodeConsoleSnapshot(
    NodeConsoleError Error,
    string? ExtUdp,
    string? ExtUdpIp,
    string? Callsign,
    string? BusyHost = null,
    IReadOnlyDictionary<string, string>? Values = null,
    IReadOnlyList<string>? Replies = null);

/// <summary>
/// Reads the connection-relevant settings from a node over its NET console (port 2323, HMAC) and – only when the
/// user confirms – sends the two commands that make ext-UDP reach this PC.
/// <para>
/// It is deliberately narrow: only <c>--extudp on</c>, <c>--extudpip &lt;IPv4&gt;</c> and the three KISS switches
/// <c>--kiss on</c> / <c>--kiss tx on</c> / <c>--kiss meta on</c> and <c>--setcall CALL-SSID</c> can be sent; everything else
/// stays untouched. <c>--setcall</c> changes the node's identity and may restart it, so callers must warn about that. It borrows the shared <see cref="HmacConsoleService"/> only while idle and
/// disconnects afterwards, and it never takes over a console that is connected to another host.
/// </para>
/// </summary>
public sealed partial class NodeConsoleFixService
{
    private readonly HmacConsoleService _hmac;
    private readonly ConsoleCommandHelperService _console;
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly ILogger<NodeConsoleFixService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NodeConsoleFixService(
        HmacConsoleService hmac,
        ConsoleCommandHelperService console,
        IOptionsMonitor<MeshcomSettings> settings,
        ILogger<NodeConsoleFixService> logger)
    {
        _hmac     = hmac;
        _console  = console;
        _settings = settings;
        _logger   = logger;
    }

    /// <summary>TCP port of the node's net console (settings, default 2323).</summary>
    public int Port => _settings.CurrentValue.TelnetPort;

    /// <summary>The net console password saved in the settings for the given node (primary node when null).</summary>
    public string SavedPassword(Guid? nodeId)
    {
        var s = _settings.CurrentValue;
        var node = nodeId is { } id && id != Guid.Empty
            ? s.Nodes.FirstOrDefault(n => n.Id == id)
            : s.Nodes.FirstOrDefault(n => n.IsPrimary);
        return !string.IsNullOrEmpty(node?.TelnetPassword) ? node!.TelnetPassword : s.TelnetPassword;
    }

    /// <summary>The only commands that may be sent (ext UDP target + KISS switches).</summary>
    [GeneratedRegex(@"^--(extudp on|extudpip (\d{1,3}\.){3}\d{1,3}|kiss on|kiss tx on|kiss meta on|setcall [A-Za-z0-9]{3,7}-\d{1,2})$")]
    private static partial Regex AllowedCommandPattern();

    /// <summary>True for the <c>--setcall</c> command (changes the node's callsign; the node may restart).</summary>
    public static bool IsSetCall(string command) => command.StartsWith("--setcall ", StringComparison.Ordinal);

    /// <summary>True when <paramref name="command"/> is one of the few commands WebDesk may send to a node.</summary>
    public static bool IsAllowedCommand(string command) =>
        AllowedCommandPattern().IsMatch(command) &&
        (!command.StartsWith("--extudpip ", StringComparison.Ordinal) || IPAddress.TryParse(command["--extudpip ".Length..], out _));

    /// <summary>True when the node accepts a TCP connection on the net console port (console switched on).</summary>
    public async Task<bool> IsReachableAsync(string host, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(host?.Trim(), out var ip)) return false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            using var client = new TcpClient();
            await client.ConnectAsync(ip, Port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Logs in, asks <c>--info</c> and returns what the node reports. Changes nothing.</summary>
    public Task<NodeConsoleSnapshot> ReadAsync(string host, string? password, CancellationToken ct = default) =>
        WithConsoleAsync(host, password, () => ReadValuesAsync(ct), ct);

    /// <summary>
    /// Sends the given commands (only the whitelisted ones) and returns the values read back afterwards.
    /// The caller must have the user's confirmation before calling this.
    /// </summary>
    public Task<NodeConsoleSnapshot> ApplyAsync(string host, string? password, IReadOnlyList<string> commands, CancellationToken ct = default)
    {
        foreach (var c in commands)
        {
            if (!AllowedCommandPattern().IsMatch(c) ||
                (c.StartsWith("--extudpip ", StringComparison.Ordinal) && !IPAddress.TryParse(c["--extudpip ".Length..], out _)))
                throw new ArgumentException($"Command not allowed: {c}", nameof(commands));
        }

        // The firmware only accepts "--extudp on" when the target IP is already set ("Please set EXPUDP IP first"),
        // and a callsign change may restart the node: IP first, ext UDP next, --setcall last.
        var ordered = commands.OrderBy(c => c.StartsWith("--extudpip ", StringComparison.Ordinal) ? 0 : IsSetCall(c) ? 2 : 1).ToList();

        return WithConsoleAsync(host, password, async () =>
        {
            int linesBefore;
            lock (_hmac.Lines) linesBefore = _hmac.Lines.Count;

            foreach (var c in ordered)
            {
                _logger.LogInformation("Node console fix: sending '{Command}' to {Host}", c, host);
                await _console.SendRawAsync(c);
                await Task.Delay(700, ct);
            }
            await Task.Delay(1000, ct);

            // What the node answered to the commands (refusals such as "Please set EXPUDP IP first").
            var replies = CollectReplies(linesBefore);
            // The node may have dropped the console (e.g. restarting after --setcall): nothing to read back.
            if (!_hmac.IsConnected)
                return new NodeConsoleSnapshot(NodeConsoleError.Other, null, null, null);
            var after = await ReadValuesAsync(ct);
            return replies.Count > 0 ? after with { Replies = replies } : after;
        }, ct);
    }

    /// <summary>Console lines that arrived since <paramref name="fromIndex"/> and look like a refusal or error.</summary>
    private List<string> CollectReplies(int fromIndex)
    {
        List<string> lines;
        lock (_hmac.Lines) lines = [.. _hmac.Lines.Skip(fromIndex)];
        return lines
            .Select(l => AnsiPattern().Replace(l, "").Trim())
            .Where(l => l.Length > 0 && ReplyPattern().IsMatch(l))
            .Distinct()
            .Take(4)
            .ToList();
    }

    [GeneratedRegex(@"refused|please|wrong command|unknown|invalid|error|failed|not (?:allowed|supported)", RegexOptions.IgnoreCase)]
    private static partial Regex ReplyPattern();

    private async Task<NodeConsoleSnapshot> WithConsoleAsync(
        string host, string? password, Func<Task<NodeConsoleSnapshot>> action, CancellationToken ct)
    {
        host = host.Trim();
        await _gate.WaitAsync(ct);
        var weConnected = false;
        try
        {
            if (_hmac.IsConnected)
            {
                if (!string.Equals(_hmac.ConnectedHost, host, StringComparison.OrdinalIgnoreCase))
                    return new NodeConsoleSnapshot(NodeConsoleError.Busy, null, null, null, _hmac.ConnectedHost);
            }
            else
            {
                await _hmac.ConnectAsync(host, password);
                if (!_hmac.IsConnected)
                    return new NodeConsoleSnapshot(ClassifyConnectFailure(), null, null, null);
                weConnected = true;
            }

            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Node console operation failed ({Host})", host);
            return new NodeConsoleSnapshot(NodeConsoleError.Other, null, null, null);
        }
        finally
        {
            if (weConnected)
            {
                try { await _hmac.DisconnectAsync(); } catch { /* already closed */ }
            }
            _gate.Release();
        }
    }

    /// <summary>The console service reports failures only as lines of its log; tell auth failure from the rest.</summary>
    private NodeConsoleError ClassifyConnectFailure()
    {
        string? last;
        lock (_hmac.Lines)
            last = _hmac.Lines.LastOrDefault(l => l.Contains('✗'));
        if (last is null) return NodeConsoleError.Other;
        if (last.Contains("Authentifizierung", StringComparison.OrdinalIgnoreCase)) return NodeConsoleError.AuthFailed;
        if (last.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
            last.Contains("Verbindungsfehler", StringComparison.OrdinalIgnoreCase)) return NodeConsoleError.Unreachable;
        return NodeConsoleError.Other;
    }

    /// <summary>The newest console line matching <paramref name="rx"/> (first group), ANSI codes stripped.</summary>
    private string? ReadLast(Regex rx)
    {
        List<string> lines;
        lock (_hmac.Lines) lines = [.. _hmac.Lines];
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var m = rx.Match(AnsiPattern().Replace(lines[i], ""));
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>The node's own IP from the <c>--info</c> line "...IP address   : a.b.c.d" (latest occurrence).</summary>
    private string? ReadNodeIp() => ReadLast(NodeIpPattern());

    [GeneratedRegex(@"\.\.\.IP address\s*:\s*((?:\d{1,3}\.){3}\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex NodeIpPattern();

    /// <summary>Bluetooth hub only: the port it sends to ("...EXT PORT n") and the UDP port it listens on ("...UDP PORT n").</summary>
    [GeneratedRegex(@"\.\.\.EXT PORT\s+(\d{1,5})")]
    private static partial Regex HubPeerPortPattern();

    [GeneratedRegex(@"\.\.\.UDP PORT\s+(\d{1,5})")]
    private static partial Regex HubUdpPortPattern();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiPattern();

    private async Task<NodeConsoleSnapshot> ReadValuesAsync(CancellationToken ct)
    {
        var values = _console.CurrentValues;
        foreach (var key in new[] { "extudp", "extudpip", "setcall", "kiss", "kiss tx", "kiss meta", "kiss auth", "netconsole", "gateway", "webserver", "nopmother" })
            values.Remove(key);

        // Only "--info": it carries everything compared here. RefreshAsync would also send --volt/--netconsole,
        // which the Bluetooth hub does not know ("wrong command" in its console log).
        await _console.SendRawAsync("--info");

        // The answers arrive asynchronously; wait until all three are in or the time is up. A console that
        // reports the callsign but not ext UDP (Bluetooth hub) is not waited for the full time.
        var started = DateTime.UtcNow;
        var deadline = started.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (values.ContainsKey("extudp") && values.ContainsKey("extudpip") && values.ContainsKey("setcall")) break;
            if (values.ContainsKey("setcall") && DateTime.UtcNow - started > TimeSpan.FromSeconds(2.5)) break;
            await Task.Delay(250, ct);
        }

        // The rest of the --info block arrives right behind ext UDP; give it a moment.
        await Task.Delay(400, ct);

        string? Get(string key) => values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "extudp", "extudpip", "setcall", "kiss", "kiss tx", "kiss meta", "kiss auth", "netconsole", "gateway", "webserver", "nopmother" })
            if (Get(key) is { } val) all[key] = val;
        if (ReadNodeIp() is { } ip) all["ip"] = ip;
        if (ReadLast(HubPeerPortPattern()) is { } hubPeerPort) all["hubextport"] = hubPeerPort;
        if (ReadLast(HubUdpPortPattern()) is { } hubUdpPort) all["hubudpport"] = hubUdpPort;

        return new NodeConsoleSnapshot(NodeConsoleError.None, Get("extudp"), Get("extudpip"), Get("setcall"), null, all);
    }
}
