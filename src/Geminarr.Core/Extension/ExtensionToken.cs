using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Extension;

/// <summary>Whether the extension can be heard, and if not why.</summary>
public enum ExtensionTokenStatus
{
    /// <summary>The token file is set and holds a token that is long enough.</summary>
    Ready,

    /// <summary>No token file is set: nothing that the extension sends is accepted.</summary>
    NotConfigured,

    /// <summary>The file cannot be read or is empty.</summary>
    Unreadable,

    /// <summary>The token is shorter than <see cref="ExtensionToken.MinLength"/>: it is refused rather than accepted.</summary>
    Weak,
}

public interface IExtensionToken
{
    ExtensionTokenStatus Status { get; }

    /// <summary>Whether the token that was presented is the token. False, after the same work, when none is configured.</summary>
    bool Verify(string? presented);
}

/// <summary>The token that the extension presents: a long random secret in a mounted file, read each time (rotating it needs no restart), compared as hashes so that the time does not say how much of it was right.</summary>
public sealed class ExtensionToken : IExtensionToken
{
    public const int MinLength = 32;
    public const int MaxFileBytes = 4 * 1024;

    private readonly IOptions<ExtensionOptions> _options;

    public ExtensionToken(IOptions<ExtensionOptions> options)
    {
        _options = options;
    }

    public ExtensionTokenStatus Status
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.Value.TokenFile))
            {
                return ExtensionTokenStatus.NotConfigured;
            }

            var token = Read();
            if (token is null)
            {
                return ExtensionTokenStatus.Unreadable;
            }

            return token.Length < MinLength ? ExtensionTokenStatus.Weak : ExtensionTokenStatus.Ready;
        }
    }

    public bool Verify(string? presented)
    {
        var expected = Read();
        var matches = CryptographicOperations.FixedTimeEquals(Hash(presented ?? string.Empty), Hash(expected ?? string.Empty));
        return matches && expected is { Length: >= MinLength } && !string.IsNullOrEmpty(presented);
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private string? Read()
    {
        var path = _options.Value.TokenFile;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                return null;
            }

            var text = File.ReadAllText(path).TrimEnd('\r', '\n');
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
