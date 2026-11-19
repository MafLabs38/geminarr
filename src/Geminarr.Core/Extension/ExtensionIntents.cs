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
    private DateTimeOffset? _lastRoundAsked;

    /// <summary>Whether the operator disconnected and nobody has signed in since the sign-out was over: while it is, whatever the page was said to show is not the session.</summary>
    public bool Disconnected { get; private set; }

    /// <summary>Whether the extension has been asked to sign the account out and has not yet said that nobody is signed in.</summary>
    public bool SignOutPending => _signOutAsked;

    /// <summary>A person is about to sign in: the extension is asked for rounds, quickly. A pending sign-out is not touched (it wins until it is done, or until the operator cancels it).</summary>
    public void BeginSigningIn(DateTimeOffset now) => _signingInSince = now;

    /// <summary>The operator gives the pending sign-out up (it never completed, or was a mistake): nothing more is asked of the extension, and the account that is signed in is the account again.</summary>
    public void CancelSignOut() => _signOutAsked = false;

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
    /// What the extension is asked when it says that it is there: a round now (it was asked for, it has never been asked and nothing is known yet: after a restart, what it said last and what it was last asked are both older than the interval of the rounds, or a person is signing in and
    /// what it said last is old) and the sign-out. A round is asked at most once in an interval whatever the page answers: when the assistant's page does not answer (a block, an outage) the extension must not be sent to
    /// it again at every heartbeat. A round that the operator asked for is asked once, at once.
    /// </summary>
    public (bool Round, bool SignOut) TakeHints(DateTimeOffset now, ExtensionSession? session, TimeSpan roundEvery)
    {
        var sinceAsked = _lastRoundAsked is { } asked ? now - asked : TimeSpan.MaxValue;
        var sinceSaid = session is null ? TimeSpan.MaxValue : now - session.At;
        var due = sinceSaid >= roundEvery && sinceAsked >= roundEvery;
        var signingIn = SigningIn(now, session) && sinceSaid >= SigningInRoundEvery && sinceAsked >= SigningInRoundEvery;
        var round = _roundAsked || due || signingIn;
        _roundAsked = false;
        if (round)
        {
            _lastRoundAsked = now;
        }

        return (round, _signOutAsked);
    }

}
