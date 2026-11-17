namespace Geminarr.Core.Connection;

/// <summary>Whether a person is signing in to the browser right now (a tab of it is on a sign-in page), as the watcher of the tabs last saw it. In memory only: the Connection page shows it as "signing in".</summary>
public sealed class SignInObserver
{
    private volatile bool _signingIn;

    public bool SigningIn => _signingIn;

    public void Set(bool signingIn) => _signingIn = signingIn;
}
