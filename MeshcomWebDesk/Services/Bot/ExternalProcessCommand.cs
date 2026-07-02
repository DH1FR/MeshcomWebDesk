using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services.Bot;

/// <summary>
/// A user-defined bot command that delegates execution to an external process.
/// The process receives a JSON payload on stdin and returns the reply text on stdout.
/// Requires a valid application license; unlicensed instances return an error message.
/// </summary>
internal sealed class ExternalProcessCommand : IBotCommand
{
    private readonly BotCommandEntry                   _entry;
    private readonly ExternalProcessRunner             _runner;
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly AppLicenseService                 _license;

    public ExternalProcessCommand(
        BotCommandEntry                   entry,
        ExternalProcessRunner             runner,
        IOptionsMonitor<MeshcomSettings> settings,
        AppLicenseService                 license)
    {
        _entry    = entry;
        _runner   = runner;
        _settings = settings;
        _license  = license;
    }

    public string Name        => _entry.Name;
    public string Description => !string.IsNullOrWhiteSpace(_entry.Description)
                                     ? _entry.Description
                                     : _entry.Name;

    public Task<string> ExecuteAsync(string[] args, string senderCallsign) =>
        ExecuteAsync(args, senderCallsign, null);

    public async Task<string> ExecuteAsync(string[] args, string senderCallsign, MeshcomMessage? context)
    {
        if (!_license.IsLicensed)
            return $">{_entry.Name}: external commands require a license.";

        var json = ExternalProcessRunner.BuildBotPayload(
            _entry.Name, args, senderCallsign, context, _settings.CurrentValue);

        var result = await _runner.RunAsync(
            _settings.CurrentValue.BotExternalCommandsPath,
            _entry.ExternalFileName, json, _entry.TimeoutSeconds);

        return result ?? string.Empty;
    }
}
