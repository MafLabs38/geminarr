using System.Text;

namespace Geminarr.Core.Accounts;

/// <summary>The name of the Mnemarr machine of an account, made from its address so that nobody types it (SPEC §8.1): <c>geminarr-</c> and the part before the <c>@</c>.</summary>
public static class MachineName
{
    public const string Prefix = "geminarr-";

    /// <summary>The longest name that Mnemarr takes.</summary>
    public const int MaxLength = 40;

    /// <summary>How many characters of the key are added to a name that is taken.</summary>
    private const int KeyCharacters = 4;

    /// <summary>The hyphen and the characters of the key.</summary>
    private const int SuffixLength = KeyCharacters + 1;

    /// <summary>
    /// The name for an address: lower case, every character that is not a letter or a digit made a hyphen (several in a row are one), at most <see cref="MaxLength"/> characters, starting and ending with a letter or a digit.
    /// When <paramref name="isTaken"/> says that another account has it, the first four characters of the key of this account are added after a hyphen.
    /// </summary>
    public static string Of(string address, string key, Func<string, bool> isTaken)
    {
        var start = Prefix + Local(address);
        var name = Cut(start, MaxLength);
        return isTaken(name) ? Cut(start, MaxLength - SuffixLength) + "-" + key[..KeyCharacters] : name;
    }

    private static string Local(string address)
    {
        var at = address.IndexOf('@', StringComparison.Ordinal);
        var local = (at >= 0 ? address[..at] : address).Trim().ToLowerInvariant();
        var clean = new StringBuilder(local.Length);
        foreach (var character in local)
        {
            var wanted = character is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? character : '-';
            if (wanted != '-' || (clean.Length > 0 && clean[^1] != '-'))
            {
                clean.Append(wanted);
            }
        }

        var result = clean.ToString().Trim('-');
        return result.Length == 0 ? "account" : result;
    }

    private static string Cut(string name, int length) => (name.Length <= length ? name : name[..length]).TrimEnd('-');
}
