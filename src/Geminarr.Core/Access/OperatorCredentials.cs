using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Access;

/// <summary>Whether somebody can sign in, and if not why: the same words as the health check.</summary>
public enum OperatorStatus
{
    /// <summary>The name and the password file are set, and the password is long enough.</summary>
    Ready,

    /// <summary>No operator is configured: nobody can sign in, and everything but the probes stays closed.</summary>
    NotConfigured,

    /// <summary>The password file cannot be read or is empty.</summary>
    PasswordUnreadable,

    /// <summary>The password is shorter than <see cref="OperatorCredentials.MinPasswordLength"/>: it is refused rather than accepted.</summary>
    PasswordWeak,
}

public interface IOperatorCredentials
{
    OperatorStatus Status { get; }

    /// <summary>The configured name, or null. It is the key under which failures are counted as an account's.</summary>
    string? Name { get; }

    /// <summary>Whether the name and the password are the operator's. False, after the same work, when nobody can sign in.</summary>
    bool Verify(string name, string password);
}

/// <summary>
/// The operator of Geminarr: one name and one password, the password in a mounted file read each time (so that rotating the secret needs no restart).
/// The comparison takes the same time whatever is wrong: both values are compared as hashes, and both comparisons are always made.
/// </summary>
public sealed class OperatorCredentials : IOperatorCredentials
{
    public const int MinPasswordLength = 12;

    private readonly IOptions<AuthOptions> _options;

    public OperatorCredentials(IOptions<AuthOptions> options)
    {
        _options = options;
    }

    public string? Name => string.IsNullOrWhiteSpace(_options.Value.AdminUser) ? null : _options.Value.AdminUser.Trim();

    public OperatorStatus Status
    {
        get
        {
            if (Name is null || string.IsNullOrWhiteSpace(_options.Value.AdminPasswordFile))
            {
                return OperatorStatus.NotConfigured;
            }

            var password = ReadPassword();
            if (password is null)
            {
                return OperatorStatus.PasswordUnreadable;
            }

            return password.Length < MinPasswordLength ? OperatorStatus.PasswordWeak : OperatorStatus.Ready;
        }
    }

    public bool Verify(string name, string password)
    {
        var expectedPassword = ReadPassword();
        var expectedName = Name;
        // Both are compared as hashes, and both comparisons are always made: nothing says which one was wrong, or whether there is an operator at all.
        var nameMatches = CryptographicOperations.FixedTimeEquals(Hash(name), Hash(expectedName ?? string.Empty));
        var passwordMatches = CryptographicOperations.FixedTimeEquals(Hash(password), Hash(expectedPassword ?? string.Empty));
        return nameMatches && passwordMatches && expectedName is not null && expectedPassword is { Length: >= MinPasswordLength };
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    /// <summary>The password of the file, without the line break an editor or <c>echo</c> adds; null when the file cannot be read or is empty.</summary>
    private string? ReadPassword()
    {
        var path = _options.Value.AdminPasswordFile;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(path).TrimEnd('\r', '\n');
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
