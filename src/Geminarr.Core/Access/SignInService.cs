namespace Geminarr.Core.Access;

public enum SignInStatus
{
    /// <summary>A session was started.</summary>
    Signed,

    /// <summary>The name or the password is wrong, or nobody can sign in: the same answer for all of them.</summary>
    Wrong,

    /// <summary>Too many failures for that name: wait.</summary>
    Locked,
}

/// <summary>The outcome of a sign-in: the session when it was made, how long to wait when the name is locked.</summary>
public sealed record SignInOutcome(SignInStatus Status, string? SessionId = null, TimeSpan? RetryAfter = null);

/// <summary>Signs the operator in: counts the attempt, checks the name and the password, starts a session.</summary>
public sealed class SignInService
{
    /// <summary>The longest name that is looked at: a longer one is not a name, and the table of the throttle must not grow with what is typed.</summary>
    public const int MaxNameLength = 100;

    public const int MaxPasswordLength = 1_024;

    private readonly IOperatorCredentials _credentials;
    private readonly LoginThrottle _throttle;
    private readonly SessionRegistry _sessions;

    public SignInService(IOperatorCredentials credentials, LoginThrottle throttle, SessionRegistry sessions)
    {
        _credentials = credentials;
        _throttle = throttle;
        _sessions = sessions;
    }

    public SignInOutcome SignIn(string? name, string? password)
    {
        var typed = (name ?? string.Empty).Trim();
        if (typed.Length is 0 or > MaxNameLength || string.IsNullOrEmpty(password) || password.Length > MaxPasswordLength)
        {
            return new SignInOutcome(SignInStatus.Wrong);
        }

        var isOperator = string.Equals(typed, _credentials.Name, StringComparison.OrdinalIgnoreCase);
        if (!_throttle.TryReserve(typed, isOperator))
        {
            return new SignInOutcome(SignInStatus.Locked, RetryAfter: _throttle.LockedFor(typed) ?? LoginThrottle.Window);
        }

        if (!_credentials.Verify(typed, password))
        {
            return new SignInOutcome(SignInStatus.Wrong);
        }

        _throttle.Succeeded(typed);
        return new SignInOutcome(SignInStatus.Signed, _sessions.Start());
    }

    public void SignOut(string? sessionId) => _sessions.End(sessionId);
}
