namespace Geminarr.Core.Browser;

/// <summary>What is known of the browser container from outside: whether one is configured (its web desktop has an address), and whether the last look at that desktop was answered (null until one was made).</summary>
public sealed record BrowserState(bool Configured, bool? Reachable);

/// <summary>
/// The browser container (ADR 0016): an ordinary Chromium that a person signs in to by hand, whose web desktop Geminarr relays. Geminarr has no debugging port to it (ADR 0021) and holds no session: the session, the
/// account and the conversations are the extension's word (ADR 0022). All that this host does is look, over plain HTTP, whether the desktop answers, so that a container that is down is said.
/// </summary>
public interface IBrowserHost
{
    /// <summary>What the last look found, without making one.</summary>
    BrowserState State();

    /// <summary>Looks whether the desktop of the browser answers (any HTTP answer is one; nothing is followed). Without a desktop configured there is nothing to look at.</summary>
    Task<bool> ProbeAsync(CancellationToken cancellationToken);
}
