using System.Globalization;

namespace LootPose.Companion;

/// <summary>
/// The web app's week and Treasure Hour rules, written again in C#. The shared examples in
/// tests/fixtures/bucketing.json keep the two the same. Change both together.
/// </summary>
public static class Bucketing
{
    /// <summary>The period of a chest opened outside every Treasure Hour (NORMAL_PERIOD in types.ts).</summary>
    public const string NormalPeriod = "normal";

    /// <summary>The most recent weekly reset at or before <paramref name="at"/> (weekStart in weeks.ts).</summary>
    public static DateTime WeekStart(WeeklyReset reset, DateTimeOffset at)
    {
        var now = at.UtcDateTime;
        var start = new DateTime(now.Year, now.Month, now.Day, reset.Hour, reset.Minute, 0, DateTimeKind.Utc);
        var daysBack = ((int)now.DayOfWeek - (int)reset.Day + 7) % 7;
        start = start.AddDays(-daysBack);
        if (start > now) start = start.AddDays(-7);
        return start;
    }

    /// <summary>The UTC date the week started, YYYY-MM-DD (weekId in weeks.ts).</summary>
    public static string WeekId(WeeklyReset reset, DateTimeOffset at) =>
        WeekStart(reset, at).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The first Treasure Hour, in file order, that contains the instant (activeTreasureHour in schedule.ts).</summary>
    public static TreasureHour? ActiveTreasureHour(GameData data, DateTimeOffset at)
    {
        foreach (var hour in data.TreasureHours)
        {
            if (hour.Start <= at && at < hour.End) return hour;
        }
        return null;
    }

    /// <summary>True from the event's end instant on (eventEnded in schedule.ts).</summary>
    public static bool EventEnded(GameData data, DateTimeOffset at) => data.EventEnd is { } end && at >= end;

    /// <summary>"week|period" (bucketKey in counter.ts).</summary>
    public static string BucketKey(string week, string period) => $"{week}|{period}";

    /// <summary>The bucket a chest opened at <paramref name="at"/> goes in.</summary>
    public static string BucketFor(GameData data, DateTimeOffset at)
    {
        var instant = ToMilliseconds(at);
        var hour = ActiveTreasureHour(data, instant);
        return BucketKey(WeekId(data.WeeklyReset, instant), hour?.Id ?? NormalPeriod);
    }

    /// <summary>JavaScript's Date holds whole milliseconds, so the same instant is cut the same way here.</summary>
    public static DateTimeOffset ToMilliseconds(DateTimeOffset at)
    {
        var ticks = at.UtcTicks;
        return new DateTimeOffset(ticks - ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
    }
}
