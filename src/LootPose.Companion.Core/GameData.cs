using System.Globalization;
using System.Text.Json;

namespace LootPose.Companion;

/// <summary>When the week turns over: a day and a time of day, in UTC.</summary>
public sealed record WeeklyReset(DayOfWeek Day, int Hour, int Minute);

/// <summary>A Treasure Hour. The start is included and the end is not.</summary>
public sealed record TreasureHour(string Id, DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// The parts of LootPose's data file (https://lootpose.vercel.app/data/treasures.json) the companion needs.
/// The file's shape is set by the web app's src/lib/types.ts. Other fields are ignored.
/// </summary>
public sealed record GameData(WeeklyReset WeeklyReset, DateTimeOffset? EventEnd, IReadOnlyList<TreasureHour> TreasureHours)
{
    /// <summary>Reads the data file. Throws FormatException when a part the companion needs is missing or wrong.</summary>
    public static GameData Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var reset = root.GetProperty("weeklyReset");
            var day = Day(reset.GetProperty("day").GetString());
            var (hour, minute) = TimeOfDay(reset.GetProperty("timeUtc").GetString());
            DateTimeOffset? eventEnd = root.TryGetProperty("eventEnd", out var end) ? Instant(end.GetString()) : null;
            var hours = new List<TreasureHour>();
            foreach (var item in root.GetProperty("treasureHours").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                if (string.IsNullOrEmpty(id)) throw new FormatException("A Treasure Hour has no id.");
                hours.Add(new TreasureHour(id, Instant(item.GetProperty("start").GetString()), Instant(item.GetProperty("end").GetString())));
            }
            return new GameData(new WeeklyReset(day, hour, minute), eventEnd, hours);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException("LootPose's data file is not valid.", e);
        }
    }

    /// <summary>An ISO 8601 instant such as 2026-11-07T19:00:00Z, as UTC.</summary>
    public static DateTimeOffset Instant(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : throw new FormatException($"Not an instant: {text}");

    private static DayOfWeek Day(string? name) => name switch
    {
        "sunday" => DayOfWeek.Sunday,
        "monday" => DayOfWeek.Monday,
        "tuesday" => DayOfWeek.Tuesday,
        "wednesday" => DayOfWeek.Wednesday,
        "thursday" => DayOfWeek.Thursday,
        "friday" => DayOfWeek.Friday,
        "saturday" => DayOfWeek.Saturday,
        _ => throw new FormatException($"Not a weekday: {name}"),
    };

    /// <summary>"HH:MM" in UTC.</summary>
    private static (int Hour, int Minute) TimeOfDay(string? text)
    {
        if (text is { Length: 5 } && text[2] == ':'
            && int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            && int.TryParse(text.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
            && hour < 24 && minute < 60)
        {
            return (hour, minute);
        }
        throw new FormatException($"Not a time of day: {text}");
    }
}
