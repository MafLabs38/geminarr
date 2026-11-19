using System.Security.Cryptography;

namespace Geminarr.Core.Access;

/// <summary>A secret that nobody can guess: 256 random bits in the characters of a URL and a cookie.</summary>
internal static class SecureToken
{
    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
