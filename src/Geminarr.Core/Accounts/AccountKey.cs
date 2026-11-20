using System.Security.Cryptography;
using System.Text;

namespace Geminarr.Core.Accounts;

/// <summary>
/// What tells one account from another without keeping its address: the hash of the address, lower-cased (SPEC §8.1). It names the folder of the account, so it is hexadecimal and nothing else: no path ever comes from a page.
/// </summary>
public static class AccountKey
{
    public const int Length = 16;

    public static string Of(string address)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(address.Trim().ToLowerInvariant()));
        return Convert.ToHexStringLower(hash)[..Length];
    }

    /// <summary>
    /// The identifier of the "computer" of an account, as Mnemarr takes it (64 lower-case hexadecimal digits): the hash of the key, so it carries no address. Mnemarr binds a machine to it, and so refuses to give the machine of one
    /// account to another that asks for the same name (an account that was forgotten, another Geminarr), whatever the names are (SPEC §8.2, rule 5).
    /// </summary>
    public static string ComputerId(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("geminarr-account:" + key)));

    /// <summary>Whether a text is a key (and so can be a folder name): exactly <see cref="Length"/> lower-case hexadecimal characters.</summary>
    public static bool IsValid(string? key) => key is { Length: Length } && key.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
