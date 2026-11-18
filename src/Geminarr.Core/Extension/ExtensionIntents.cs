namespace Geminarr.Core.Extension;

/// <summary>
/// What the operator has asked of the extension, and what it is waiting for: a sign-in that a person is making, a round that was asked for, and a sign-out (the *Disconnect*). It holds no lock of its own: the inbox calls
/// it under its own. A sign-out is asked until the extension says that nobody is signed in (it is not dropped after a while: the browser may be off for longer than any delay), and until then no word of "signed in" is
/// taken for somebody signing in again, since a round that was under way when the operator disconnected read the page before the cookies were deleted.
/// </summary>
internal sealed class ExtensionIntents
{
    /// <summary>How long a sign-in that the operator started is waited for: after it, the page stops saying "signing in" and the extension stops being asked for rounds.</summary>
    public static readonly TimeSpan SigningInFor = TimeSpan.FromMinutes(10);

    /// <summary>How old what the extension said last may be, while a person signs in, before it is asked for a round again.</summary>
    public static readonly TimeSpan SigningInRoundEvery = TimeSpan.FromSeconds(20);

    private DateTimeOffset? _signingInSince;
    private bool _roundAsked;
    private bool _signOutAsked;

    /// <summary>Whether the operator disconnected and nobody has signed in since the sign-out was over: while it is, whatever the page was said to show is not the session.</summary>
    public bool Disconnected { get; private set; }

    /// <summary>Whether the extension has been asked to sign the account out and has not yet said that nobody is signed in.</summary>
    public bool SignOutPending => _signOutAsked;

    /// <summary>A person is about to sign in: whatever sign-out was pending is given up (the operator who presses *Sign in* wants an account signed in, and a sign-out that never completes must not be a loop that nothing can stop).</summary>
    public void BeginSigningIn(DateTimeOffset now)
    {
        _signingInSince = now;
        _signOutAsked = false;
    }

    public void RequestRound() => _roundAsked = true;

    public void RequestSignOut()
    {
        _signOutAsked = true;
        Disconnected = true;
        _signingInSince = null;
    }

    /// <summary>Whether a sign-in that the operator started is being waited for: it ends when the extension says that somebody is signed in, and after <see cref="SigningInFor"/>.</summary>
    public bool SigningIn(DateTimeOffset now, ExtensionSession? session) =>
        _signingInSince is { } since && now - since < SigningInFor && !(session is { SignedIn: true } signedIn && signedIn.At >= since);

    /// <summary>The extension says that somebody is signed in: the sign-in is over, and the disconnect is too unless the sign-out is still pending (the page that said so was read before it).</summary>
    public void SaidSignedIn()
    {
        _signingInSince = null;
        if (!_signOutAsked)
        {
            Disconnected = false;
        }
    }

    /// <summary>The extension says that nobody is signed in: the sign-out that was asked for is done (the disconnect lasts until somebody signs in).</summary>
    public void SaidSignedOut() => _signOutAsked = false;

    /// <summary>
    /// What the extension is asked when it says that it is there: a round now (it was asked for, what it said last is older than the interval of the rounds, a person is signing in and what it said last is old, or nothing is
    /// known yet: after a restart) and the sign-out. A round that was asked for is asked once.
    /// </summary>
    public (bool Round, bool SignOut) TakeHints(DateTimeOffset now, ExtensionSession? session, TimeSpan roundEvery)
    {
        var round = _roundAsked || session is null || now - session.At >= roundEvery || (SigningIn(now, session) && now - session.At >= SigningInRoundEvery);
        _roundAsked = false;
        return (round, _signOutAsked);
    }
}
