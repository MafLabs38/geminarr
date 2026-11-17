namespace Geminarr.Core.Connection;

/// <summary>
/// Whether a person is signing in to the browser right now (a tab of it is on a sign-in page), as the watcher of the tabs last saw it, and when a sign-in last ended. In memory only: the Connection page shows it as "signing in",
/// and keeps saying so for a few seconds after the tab is gone, while the check that follows finds the session (it would say "not signed in" for that moment otherwise).
/// </summary>
public sealed class SignInObserver
{
    private readonly TimeProvider _clock;
    private readonly Lock _lock = new();
    private bool _signingIn;
    private DateTimeOffset? _endedAt;

    public SignInObserver(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public bool SigningIn
    {
        get
        {
            lock (_lock)
            {
                return _signingIn;
            }
        }
    }

    public void Set(bool signingIn)
    {
        lock (_lock)
        {
            if (_signingIn && !signingIn)
            {
                _endedAt = _clock.GetUtcNow();
            }

            _signingIn = signingIn;
        }
    }

    /// <summary>Whether a sign-in ended less than this long ago.</summary>
    public bool EndedWithin(TimeSpan span)
    {
        lock (_lock)
        {
            return _endedAt is { } ended && _clock.GetUtcNow() - ended < span;
        }
    }
}
