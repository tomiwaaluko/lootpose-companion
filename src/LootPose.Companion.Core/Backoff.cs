namespace LootPose.Companion;

/// <summary>After a failed push: wait 30 seconds, then twice as long each time, up to 15 minutes. A success starts over.</summary>
public sealed class Backoff
{
    public static readonly TimeSpan First = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Longest = TimeSpan.FromMinutes(15);

    private TimeSpan _next = First;

    public DateTimeOffset? NotBefore { get; private set; }

    public bool Allows(DateTimeOffset now) => NotBefore is not { } wait || now >= wait;

    /// <summary><paramref name="atLeast"/> is the server's retryAfter, when it gave one.</summary>
    public void Failed(DateTimeOffset now, TimeSpan? atLeast = null)
    {
        var delay = atLeast is { } minimum && minimum > _next ? minimum : _next;
        NotBefore = now + delay;
        _next = TimeSpan.FromTicks(Math.Min(_next.Ticks * 2, Longest.Ticks));
    }

    public void Succeeded()
    {
        NotBefore = null;
        _next = First;
    }
}
