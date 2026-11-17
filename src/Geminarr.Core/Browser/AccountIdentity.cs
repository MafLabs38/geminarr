using System.Text.RegularExpressions;

namespace Geminarr.Core.Browser;

/// <summary>
/// Who the browser is signed in as, as the assistant's page says it for its own account button: an address, and a name when the page gives one. It is the operator's to see on the Connection page and nothing else's:
/// it is never logged, never in the heartbeat, never sent to Mnemarr, and it lives in memory only (the next check reads it again).
/// </summary>
public sealed record AccountIdentity(string? Name, string Email);

/// <summary>Reads the label that the assistant's page gives to the account button (<c>"Google Account: Jane Doe (jane@example.org)"</c>, in the language of the page).</summary>
public static partial class AccountLabel
{
    /// <summary>
    /// The address is what sits in parentheses and has an <c>@</c>. The name is what stands before it, after the colon that the page puts after the words "Google Account" (a label without a colon says no name: it is not
    /// guessed from words of a language).
    /// </summary>
    public static AccountIdentity? Parse(IEnumerable<string?> labels)
    {
        var parsed = labels.Where(label => !string.IsNullOrWhiteSpace(label)).Select(label => Parse(label!)).Where(identity => identity is not null).ToList();
        var first = parsed.FirstOrDefault();
        return first is null ? null : first with { Name = parsed.Select(identity => identity!.Name).FirstOrDefault(name => name is not null) };
    }

    private static AccountIdentity? Parse(string label)
    {
        var address = Address().Match(label);
        if (!address.Success)
        {
            return null;
        }

        var before = label[..address.Index];
        var colon = before.IndexOf(':', StringComparison.Ordinal);
        var name = colon < 0 ? null : before[(colon + 1)..].Trim();
        return new AccountIdentity(string.IsNullOrEmpty(name) ? null : name, address.Groups[1].Value);
    }

    [GeneratedRegex(@"\(([^()\s@]+@[^()\s@]+)\)", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex Address();
}
