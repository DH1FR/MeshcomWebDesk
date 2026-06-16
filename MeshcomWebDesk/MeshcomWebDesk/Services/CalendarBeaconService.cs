using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Background service that checks configured calendar beacon entries every minute
/// and sends announcement messages at the configured lead times (days before,
/// hours before, and/or at the event itself).
///
/// Each send slot is tracked in-memory to prevent duplicate transmissions within
/// the same run. A restart only skips the current time slot (same as telemetry).
/// </summary>
public sealed class CalendarBeaconService : BackgroundService
{
    private readonly ILogger<CalendarBeaconService> _logger;
    private readonly IOptionsMonitor<MeshcomSettings> _optionsMonitor;
    private readonly IMeshcomSender _sender;
    private readonly IMeshcomVariableExpander _expander;
    private readonly ExternalProcessRunner _runner;

    /// <summary>
    /// Tracks slots that have already been sent this session.
    /// Key format: "{entryId}:{slotTag}" where slotTag is e.g. "2025-06-06-T", "2025-06-06-3d", "2025-06-06-2h".
    /// </summary>
    private readonly HashSet<string> _sentSlots = [];

    public CalendarBeaconService(
        ILogger<CalendarBeaconService> logger,
        IOptionsMonitor<MeshcomSettings> optionsMonitor,
        IMeshcomSender sender,
        IMeshcomVariableExpander expander,
        ExternalProcessRunner runner)
    {
        _logger         = logger;
        _optionsMonitor = optionsMonitor;
        _sender         = sender;
        _expander       = expander;
        _runner         = runner;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pre-seed sent slots with current minute so we don't fire immediately on startup
        // for events that happen to fall exactly now.
        SeedCurrentSlots();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            await CheckAndSendAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Calculates and fires any pending announcements for the current minute.
    /// Called once per minute from the loop.
    /// </summary>
    private async Task CheckAndSendAsync(CancellationToken stoppingToken)
    {
        var settings = _optionsMonitor.CurrentValue;
        var entries  = settings.CalendarBeacons;
        if (entries.Count == 0) return;

        var now      = DateTime.Now;
        var cooldown = TimeSpan.FromSeconds(Math.Max(0, settings.TxCooldownSeconds));
        bool lastSent = false;

        foreach (var entry in entries)
        {
            if (!entry.Enabled) continue;
            if (string.IsNullOrWhiteSpace(entry.Group)) continue;
            if (!entry.IsExternal && string.IsNullOrWhiteSpace(entry.Text)) continue;

            try
            {
                if (lastSent && cooldown > TimeSpan.Zero)
                    await Task.Delay(cooldown, stoppingToken);

                lastSent = await ProcessEntryAsync(entry, now);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CalendarBeacon: Fehler bei Eintrag \"{Title}\" ({Id})", entry.Title, entry.Id);
                lastSent = false;
            }
        }
    }

    private async Task<bool> ProcessEntryAsync(CalendarBeaconEntry entry, DateTime now)
    {
        var today    = DateOnly.FromDateTime(now);
        var nextDate = GetNextOccurrence(entry, today);

        if (nextDate == DateOnly.MaxValue) return false;

        var eventDt = nextDate.ToDateTime(entry.EventTimeParsed);
        bool sent   = false;

        // ── Ankündigung: X Tage vorher ────────────────────────────────────
        if (entry.AnnounceLeadDays > 0)
        {
            var leadDt    = eventDt.AddDays(-entry.AnnounceLeadDays);
            var slotKey   = $"{entry.Id}:{nextDate:yyyy-MM-dd}-{entry.AnnounceLeadDays}d";
            if (IsCurrentMinute(leadDt, now) && _sentSlots.Add(slotKey))
            {
                var daysUntil = (int)Math.Round((eventDt - now).TotalDays);
                await SendAsync(entry, nextDate, now, daysUntil);
                sent = true;
            }
        }

        // ── Ankündigung: X Stunden vorher ─────────────────────────────────
        if (entry.AnnounceLeadHours > 0)
        {
            var leadDt  = eventDt.AddHours(-entry.AnnounceLeadHours);
            var slotKey = $"{entry.Id}:{nextDate:yyyy-MM-dd}-{entry.AnnounceLeadHours}h";
            if (IsCurrentMinute(leadDt, now) && _sentSlots.Add(slotKey))
            {
                var hoursUntil = (int)Math.Round((eventDt - now).TotalHours);
                await SendAsync(entry, nextDate, now, hoursUntil: hoursUntil);
                sent = true;
            }
        }

        // ── Zum Terminzeitpunkt selbst ────────────────────────────────────
        if (entry.AnnounceAtEvent)
        {
            var slotKey = $"{entry.Id}:{nextDate:yyyy-MM-dd}-T";
            if (IsCurrentMinute(eventDt, now) && _sentSlots.Add(slotKey))
            {
                await SendAsync(entry, nextDate, now, daysUntil: 0, hoursUntil: 0);
                sent = true;
            }
        }

        return sent;
    }

    /// <summary>Returns true when <paramref name="target"/> falls within the current minute window.</summary>
    private static bool IsCurrentMinute(DateTime target, DateTime now) =>
        target.Year   == now.Year  &&
        target.Month  == now.Month &&
        target.Day    == now.Day   &&
        target.Hour   == now.Hour  &&
        target.Minute == now.Minute;

    private async Task SendAsync(
        CalendarBeaconEntry entry,
        DateOnly            eventDate,
        DateTime            now,
        int?                daysUntil  = null,
        int?                hoursUntil = null)
    {
        var text = await BuildTextAsync(entry, eventDate, now, daysUntil, hoursUntil);

        if (string.IsNullOrWhiteSpace(text))
        {
            if (entry.IsExternal)
                _logger.LogWarning(
                    "CalendarBeacon \"{Title}\": external process returned no text, skipping.",
                    entry.Title);
            return;
        }

        var dest = entry.Group.TrimStart('#');
        var tab  = entry.Group.StartsWith('#') ? entry.Group : null;

        _logger.LogInformation(
            "CalendarBeacon \"{Title}\" → {Group}: {Text}",
            entry.Title, entry.Group, text);

        await _sender.SendMessageAsync(dest, text, tab);
    }

    /// <summary>
    /// Returns the beacon text: calls an external process when <see cref="CalendarBeaconEntry.IsExternal"/>
    /// is true, otherwise expands placeholders in the static <see cref="CalendarBeaconEntry.Text"/>.
    /// </summary>
    private async Task<string?> BuildTextAsync(
        CalendarBeaconEntry entry,
        DateOnly            eventDate,
        DateTime            now,
        int?                daysUntil,
        int?                hoursUntil)
    {
        if (entry.IsExternal)
        {
            var settings = _optionsMonitor.CurrentValue;
            var json     = ExternalProcessRunner.BuildCalendarBeaconPayload(
                               entry, eventDate, daysUntil, hoursUntil, settings);
            return await _runner.RunAsync(
                       settings.BotExternalCommandsPath,
                       entry.ExternalFileName, json, entry.TimeoutSeconds);
        }

        // Static text with placeholder expansion
        var text    = _expander.ExpandVariables(entry.Text);
        var eventDt = eventDate.ToDateTime(entry.EventTimeParsed);
        var dDays   = daysUntil  ?? (int)Math.Max(0, Math.Round((eventDt - now).TotalDays));
        var dHours  = hoursUntil ?? (int)Math.Max(0, Math.Round((eventDt - now).TotalHours));

        return text
            .Replace("{title}",       entry.Title,                      StringComparison.OrdinalIgnoreCase)
            .Replace("{event_date}",  eventDate.ToString("dd.MM.yyyy"), StringComparison.OrdinalIgnoreCase)
            .Replace("{event_time}",  entry.EventTime,                  StringComparison.OrdinalIgnoreCase)
            .Replace("{days_until}",  dDays.ToString(),                 StringComparison.OrdinalIgnoreCase)
            .Replace("{hours_until}", dHours.ToString(),                StringComparison.OrdinalIgnoreCase);
    }

    // ── Recurrence calculation ────────────────────────────────────────────

    /// <summary>
    /// Returns the next occurrence date on or after <paramref name="from"/>.
    /// Returns <see cref="DateOnly.MaxValue"/> when no valid date can be computed.
    /// </summary>
    public static DateOnly GetNextOccurrence(CalendarBeaconEntry e, DateOnly from)
    {
        return e.RecurrenceType switch
        {
            CalendarRecurrence.Once        => e.ReferenceDateParsed ?? DateOnly.MaxValue,
            CalendarRecurrence.Weekly      => NextWeekly(from, e.EventDayOfWeek),
            CalendarRecurrence.BiWeekly    => NextBiWeekly(from, e.EventDayOfWeek, e.ReferenceDateParsed),
            CalendarRecurrence.Monthly     => NextMonthly(from, e.EventDayOfMonth),
            CalendarRecurrence.NthWeekday  => NextNthWeekday(from, e.EventDayOfWeek, e.WeekdayOrdinal),
            CalendarRecurrence.LastWeekday => NextLastWeekday(from, e.EventDayOfWeek),
            _                              => DateOnly.MaxValue
        };
    }

    // ── Weekly ───────────────────────────────────────────────────────────

    private static DateOnly NextWeekly(DateOnly from, DayOfWeek dow)
    {
        int diff = ((int)dow - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(diff);
    }

    // ── BiWeekly ─────────────────────────────────────────────────────────

    private static DateOnly NextBiWeekly(DateOnly from, DayOfWeek dow, DateOnly? reference)
    {
        // Without a reference date fall back to weekly behaviour.
        if (reference is null)
            return NextWeekly(from, dow);

        // Find the first occurrence of dow on or after 'from'.
        var candidate = NextWeekly(from, dow);

        // Determine parity based on number of weeks since the reference date.
        // If candidate is in the same 2-week cycle as reference → use it; else add 7 days.
        int weekDiff = (candidate.DayNumber - reference.Value.DayNumber) / 7;
        if (weekDiff % 2 != 0)
            candidate = candidate.AddDays(7);

        return candidate;
    }

    // ── Monthly ──────────────────────────────────────────────────────────

    private static DateOnly NextMonthly(DateOnly from, int dayOfMonth)
    {
        // Clamp to valid day in the current month
        var clamped = ClampDay(from.Year, from.Month, dayOfMonth);
        if (clamped >= from) return clamped;

        // Move to next month
        var next = from.AddMonths(1);
        return ClampDay(next.Year, next.Month, dayOfMonth);
    }

    private static DateOnly ClampDay(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    // ── NthWeekday ───────────────────────────────────────────────────────

    private static DateOnly NextNthWeekday(DateOnly from, DayOfWeek dow, int n)
    {
        var candidate = GetNthWeekday(from.Year, from.Month, dow, n);
        if (candidate >= from) return candidate;

        var next = from.AddMonths(1);
        return GetNthWeekday(next.Year, next.Month, dow, n);
    }

    /// <summary>
    /// Returns the n-th occurrence of <paramref name="dow"/> in the given month/year.
    /// If n exceeds the number of that weekday in the month, the last occurrence is returned.
    /// </summary>
    public static DateOnly GetNthWeekday(int year, int month, DayOfWeek dow, int n)
    {
        var first = new DateOnly(year, month, 1);
        int diff  = ((int)dow - (int)first.DayOfWeek + 7) % 7;
        var firstOccurrence = first.AddDays(diff);

        // Clamp: never exceed the last day of the month
        var candidate = firstOccurrence.AddDays((n - 1) * 7);
        while (candidate.Month != month)
            candidate = candidate.AddDays(-7);

        return candidate;
    }

    // ── LastWeekday ──────────────────────────────────────────────────────

    private static DateOnly NextLastWeekday(DateOnly from, DayOfWeek dow)
    {
        var candidate = GetLastWeekday(from.Year, from.Month, dow);
        if (candidate >= from) return candidate;

        var next = from.AddMonths(1);
        return GetLastWeekday(next.Year, next.Month, dow);
    }

    /// <summary>Returns the last occurrence of <paramref name="dow"/> in the given month/year.</summary>
    public static DateOnly GetLastWeekday(int year, int month, DayOfWeek dow)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        int diff = ((int)last.DayOfWeek - (int)dow + 7) % 7;
        return last.AddDays(-diff);
    }

    // ── Startup seed ─────────────────────────────────────────────────────

    /// <summary>
    /// Pre-fills sent slots with entries that would fire in the current minute,
    /// so that a service restart does not immediately re-send a beacon.
    /// </summary>
    private void SeedCurrentSlots()
    {
        var now     = DateTime.Now;
        var today   = DateOnly.FromDateTime(now);
        var entries = _optionsMonitor.CurrentValue.CalendarBeacons;

        foreach (var entry in entries)
        {
            if (!entry.Enabled) continue;
            var nextDate = GetNextOccurrence(entry, today);
            if (nextDate == DateOnly.MaxValue) continue;
            var eventDt = nextDate.ToDateTime(entry.EventTimeParsed);

            if (entry.AnnounceLeadDays > 0 && IsCurrentMinute(eventDt.AddDays(-entry.AnnounceLeadDays), now))
                _sentSlots.Add($"{entry.Id}:{nextDate:yyyy-MM-dd}-{entry.AnnounceLeadDays}d");

            if (entry.AnnounceLeadHours > 0 && IsCurrentMinute(eventDt.AddHours(-entry.AnnounceLeadHours), now))
                _sentSlots.Add($"{entry.Id}:{nextDate:yyyy-MM-dd}-{entry.AnnounceLeadHours}h");

            if (entry.AnnounceAtEvent && IsCurrentMinute(eventDt, now))
                _sentSlots.Add($"{entry.Id}:{nextDate:yyyy-MM-dd}-T");
        }
    }
}
