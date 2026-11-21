namespace Geminarr.Core.Accounts;

/// <summary>What is asked is about the link of an account, and no account is signed in (or the last one was disconnected): the operator is told to sign one in first. Nothing else is said by this type.</summary>
public sealed class NoAccountException : InvalidOperationException
{
    public NoAccountException(string message)
        : base(message)
    {
    }
}
