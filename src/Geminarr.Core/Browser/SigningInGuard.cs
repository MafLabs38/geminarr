namespace Geminarr.Core.Browser;

/// <summary>
/// Decides whether a pass steps aside because a person is signing in: it does for a number of passes in a row, and no longer. A tab left on a sign-in page (an account picker, a prompt that was never
/// finished) is not somebody typing, and a pass that kept away from it would never see that the session is gone or fine: it looks at the session as usual instead, and measures it.
/// </summary>
public sealed class SigningInGuard
{
    private readonly Lock _gate = new();
    private int _asides;

    /// <summary>Whether this pass steps aside, given whether a tab is on a sign-in page now; the count starts again when none is.</summary>
    public bool StepsAside(bool aTabIsOnASignInPage, int allowedPasses)
    {
        lock (_gate)
        {
            if (!aTabIsOnASignInPage)
            {
                _asides = 0;
                return false;
            }

            return ++_asides <= allowedPasses;
        }
    }
}
