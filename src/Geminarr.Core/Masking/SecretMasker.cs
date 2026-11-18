using System.Text.RegularExpressions;

namespace Geminarr.Core.Masking;

/// <summary>
/// Hides what looks like a secret in a text before anything of it is kept (SPEC 5.2, CLAUDE.md rule 7): a conversation is the assistant's user's, and a person may have pasted a key, a token or a password into it.
/// The rules are those of Mnemarr's masking and of its collector, and the file of samples <c>tests/masking/samples.json</c> is run against all of them, so that engines written by hand do not drift (Mnemarr's Q-27).
/// Mnemarr masks again on its side; this is the first line, so that the outbox (a file on a volume that may be backed up) never holds one. A rule that takes too long throws <see cref="RegexMatchTimeoutException"/>:
/// the caller refuses the text, it never keeps one it could not mask.
/// </summary>
public static partial class SecretMasker
{
    private const int LimitMilliseconds = 2000;

    /// <summary>
    /// Put after each line break or tab written as two characters (<c>\n</c>) while the rules are applied, and taken out again: without it there is no word boundary between that <c>n</c> and a token that starts the next line,
    /// which is the usual shape of a pasted token (the output of <c>cat .env</c>) in a text that a JSON line carries.
    /// </summary>
    private const char Boundary = '\u0001';

    /// <summary>The text with every secret it contains replaced by a word that says what was there.</summary>
    public static string Mask(string text)
    {
        text = Escapes().Replace(text.Replace(Boundary.ToString(), string.Empty, StringComparison.Ordinal), "$&" + Boundary);
        text = PrivateKey().Replace(text, "[masked:private-key]");
        text = GithubFineGrained().Replace(text, "[masked:github-token]");
        text = GithubToken().Replace(text, "[masked:github-token]");
        text = ApiKey().Replace(text, "[masked:api-key]");
        text = AwsKey().Replace(text, "[masked:aws-key]");
        text = SlackToken().Replace(text, "[masked:slack-token]");
        text = Jwt().Replace(text, "[masked:jwt]");
        text = GoogleApiKey().Replace(text, "[masked:api-key]");
        text = GoogleOAuthToken().Replace(text, "[masked:token]");
        text = BearerToken().Replace(text, "Bearer [masked:token]");
        text = AddressCredentials().Replace(text, "$1[masked:credentials]@");
        text = Assignment().Replace(text, "$1$2[masked]");
        return text.Replace(Boundary.ToString(), string.Empty, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\\[nrtbf]", RegexOptions.CultureInvariant, LimitMilliseconds)]
    private static partial Regex Escapes();

    [GeneratedRegex("-----BEGIN [A-Z ]*PRIVATE KEY-----.*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant | RegexOptions.Singleline, LimitMilliseconds)]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"\bgithub_pat_[A-Za-z0-9_]{40,}", RegexOptions.CultureInvariant, LimitMilliseconds)]
    private static partial Regex GithubFineGrained();

    [GeneratedRegex(@"\bgh[pousr]_[A-Za-z0-9]{30,}", RegexOptions.CultureInvariant, LimitMilliseconds)]
    private static partial Regex GithubToken();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_-]{20,}", RegexOptions.CultureInvariant, LimitMilliseconds)]
    private static partial Regex ApiKey();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b", RegexOptions.CultureInvariant, LimitMilliseconds)]
    private static partial Regex AwsKey();

    [GeneratedRegex(@"\bxox[abprs]-[A-Za-z0-9-]{10,}", RegexOptions.CultureInvariant, LimitMilliseconds)]
    private static partial Regex SlackToken();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", RegexOptions.CultureInvariant, LimitMilliseconds)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\bAIza[0-9A-Za-z_-]{35}\b", RegexOptions.CultureInvariant, LimitMilliseconds)]
    private static partial Regex GoogleApiKey();

    // An access token (ya29.) or a refresh token (1//0): what a person asking Gemini about a script is most likely to paste.
    [GeneratedRegex(@"\b(?:ya29\.[0-9A-Za-z_-]{20,}|1//0[0-9A-Za-z_-]{30,})", RegexOptions.CultureInvariant, LimitMilliseconds)]
    private static partial Regex GoogleOAuthToken();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/=-]{20,}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, LimitMilliseconds)]
    private static partial Regex BearerToken();

    // Any scheme: a connection string (postgres://, mongodb+srv://, redis://) holds a password as an address does. A scheme is short: unbounded, a very long run of such characters would be scanned from every position.
    [GeneratedRegex(@"(\b[a-z][a-z0-9+.-]{1,30}://)[^/\s:@]+:[^/\s@]+@", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, LimitMilliseconds)]
    private static partial Regex AddressCredentials();

    [GeneratedRegex(
        @"\b([A-Z0-9_]*(?:PASSWORD|PASSWD|SECRET|TOKEN|API_?KEY|ACCESS_?KEY|PRIVATE_?KEY)[A-Z0-9_]*)((?:\\?[""'])?\s*[=:]\s*(?:\\?[""'])?)[^\s""',;\\]{8,}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        LimitMilliseconds)]
    private static partial Regex Assignment();
}
