using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DeveloperTools.Tools;

public sealed record TimeParse(DateTimeOffset Value, string Interpretation);

/// <summary>Parses Unix timestamps (s/ms/µs/ns) and date strings, and formats an instant many ways.</summary>
public static partial class TimeTools
{
    [GeneratedRegex(@"^-?\d+$")]
    private static partial Regex Integer();

    [GeneratedRegex(@"^-?\d+\.\d+$")]
    private static partial Regex Decimal();

    [GeneratedRegex(@"^\d{1,2}\.\d{1,2}\.\d{2,4}")]
    private static partial Regex DottedDate();

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2}|GMT|UTC)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex HasOffset();

    /// <param name="zone">Time zone for dates written without an offset.</param>
    public static TimeParse? Parse(string input, TimeZoneInfo zone)
    {
        var text = input.Trim();
        if (text.Length == 0)
            return null;
        if (text.Equals("now", StringComparison.OrdinalIgnoreCase))
            return new TimeParse(DateTimeOffset.UtcNow, "Current time");

        if (Integer().IsMatch(text) && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
        {
            var abs = Math.Abs((double)n);
            try
            {
                return abs switch
                {
                    < 1e11 => new TimeParse(DateTimeOffset.FromUnixTimeSeconds(n), "Unix time in seconds"),
                    < 1e14 => new TimeParse(DateTimeOffset.FromUnixTimeMilliseconds(n), "Unix time in milliseconds"),
                    < 1e17 => new TimeParse(DateTimeOffset.UnixEpoch.AddTicks(n * 10), "Unix time in microseconds"),
                    _ => new TimeParse(DateTimeOffset.UnixEpoch.AddTicks(n / 100), "Unix time in nanoseconds")
                };
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        if (Decimal().IsMatch(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
        {
            try
            {
                return new TimeParse(DateTimeOffset.UnixEpoch.AddTicks((long)(secs * TimeSpan.TicksPerSecond)), "Unix time in seconds (with fraction)");
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        if (HasOffset().IsMatch(text))
        {
            foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.CurrentCulture })
                if (DateTimeOffset.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out var withOffset))
                    return new TimeParse(withOffset, "Date with time zone offset");
        }

        // Dotted dates (01.05.2024) are day-first nearly everywhere they are used.
        var dayFirst = DottedDate().IsMatch(text);
        var cultures = dayFirst
            ? new[] { CultureInfo.GetCultureInfo("tr-TR"), CultureInfo.CurrentCulture, CultureInfo.InvariantCulture }
            : new[] { CultureInfo.InvariantCulture, CultureInfo.CurrentCulture, CultureInfo.GetCultureInfo("tr-TR") };
        foreach (var culture in cultures)
        {
            if (DateTime.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out var local))
            {
                var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
                var offset = zone.GetUtcOffset(unspecified);
                return new TimeParse(new DateTimeOffset(unspecified, offset), $"Date in {ZoneName(zone)}");
            }
        }

        return null;
    }

    public static string ZoneName(TimeZoneInfo zone) =>
        zone.Id == TimeZoneInfo.Local.Id ? "local time" : zone.Id == "UTC" ? "UTC" : zone.DisplayName;

    /// <summary>Label/value rows for an instant, shown in the given zone.</summary>
    public static List<(string Label, string Value)> Formats(DateTimeOffset value, TimeZoneInfo zone, DateTimeOffset now)
    {
        var inv = CultureInfo.InvariantCulture;
        var utc = value.ToUniversalTime();
        var local = TimeZoneInfo.ConvertTime(value, zone);
        return
        [
            ("Unix seconds", value.ToUnixTimeSeconds().ToString(inv)),
            ("Unix milliseconds", value.ToUnixTimeMilliseconds().ToString(inv)),
            ("ISO 8601 (UTC)", utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", inv)),
            ($"ISO 8601 ({ShortZone(zone)})", local.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", inv)),
            ("RFC 1123 (HTTP)", utc.ToString("r", inv)),
            ("Readable", local.ToString("dddd, d MMMM yyyy  HH:mm:ss", inv) + "  " + local.ToString("zzz", inv)),
            ("Relative", Relative(value, now)),
            ("SQL", local.ToString("yyyy-MM-dd HH:mm:ss", inv)),
            ("Day / week", $"Day {local.DayOfYear} of the year, ISO week {ISOWeek.GetWeekOfYear(local.DateTime)}"),
            (".NET ticks (UTC)", utc.UtcTicks.ToString(inv))
        ];
    }

    private static string ShortZone(TimeZoneInfo zone) =>
        zone.Id == TimeZoneInfo.Local.Id ? "local" : zone.Id == "UTC" ? "UTC" : zone.Id;

    public static string Relative(DateTimeOffset value, DateTimeOffset now)
    {
        var span = value - now;
        var past = span < TimeSpan.Zero;
        var a = past ? span.Negate() : span;
        string text = a.TotalSeconds switch
        {
            < 5 => "just now",
            < 90 => $"{(int)a.TotalSeconds} seconds",
            < 5400 => $"{(int)Math.Round(a.TotalMinutes)} minutes",
            < 129600 => $"{(int)Math.Round(a.TotalHours)} hours",
            < 3888000 => $"{(int)Math.Round(a.TotalDays)} days",
            < 47304000 => $"{(int)Math.Round(a.TotalDays / 30.44)} months",
            _ => $"{a.TotalDays / 365.25:0.#} years"
        };
        return text == "just now" ? text : past ? text + " ago" : "in " + text;
    }

    /// <summary>Human-readable length of a time span ("2 days 3 hours 4 minutes").</summary>
    public static string Duration(TimeSpan span)
    {
        var a = span < TimeSpan.Zero ? span.Negate() : span;
        var parts = new List<string>();
        if (a.Days > 0) parts.Add($"{a.Days} day{(a.Days == 1 ? "" : "s")}");
        if (a.Hours > 0) parts.Add($"{a.Hours} hour{(a.Hours == 1 ? "" : "s")}");
        if (a.Minutes > 0) parts.Add($"{a.Minutes} minute{(a.Minutes == 1 ? "" : "s")}");
        if (a.Seconds > 0 || parts.Count == 0) parts.Add($"{a.Seconds} second{(a.Seconds == 1 ? "" : "s")}");
        return string.Join(" ", parts);
    }
}
