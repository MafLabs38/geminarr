namespace Geminarr.Core.Live;

/// <summary>
/// Says to the screens that something changed (ADR 0023): a topic, never the data. The screen reads what changed through the API, with its session, like any other read, so the channel is a way to be told
/// and never a way to read anything. It never throws and never waits: a screen that cannot be told must not hold up what changed.
/// </summary>
public interface ILiveNotifier
{
    void Publish(string topic);
}

/// <summary>What the screens know how to read again: each topic is a set of reads of the API (the interface maps them).</summary>
public static class LiveTopics
{
    /// <summary>The connection to the assistant: its state, who is signed in, a sign-in or a sign-out that waits, a pause.</summary>
    public const string Connection = "connection";

    /// <summary>Everything that the health and the system status say.</summary>
    public const string Status = "status";

    /// <summary>The passes and the heartbeats.</summary>
    public const string Runs = "runs";

    /// <summary>The link to Mnemarr and what waits to be sent to it.</summary>
    public const string Mnemarr = "mnemarr";
}

/// <summary>The notifier of a host that has no screen to tell (the tests, a process without the channel): nothing is sent.</summary>
public sealed class NoLiveNotifier : ILiveNotifier
{
    public void Publish(string topic)
    {
    }
}
