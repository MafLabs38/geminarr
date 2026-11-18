using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Geminarr.Core.Pipeline;

/// <summary>The lines of the <c>chat-turns</c> format (SPEC §5.2) and the names that go with them: pure functions, so that the contract is tested without a network.</summary>
public static class ConversationLines
{
    /// <summary>Mnemarr takes at most this many lines in a batch.</summary>
    public const int MaxLinesPerBatch = 500;

    /// <summary>... and at most this many bytes.</summary>
    public const int MaxBytesPerBatch = 3 * 1024 * 1024;

    /// <summary>A title is cut here (Mnemarr cuts it at the same length).</summary>
    public const int MaxTitleLength = 200;

    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The name of a conversation in Mnemarr: <c>gemini-</c> and the assistant's id, lower case, <c>[a-z0-9-]</c> only; a short hash is added when the id had to change.</summary>
    public static string Session(string id)
    {
        var original = id.Trim();
        var clean = new string([.. original.ToLowerInvariant().Select(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' ? character : '-')]).Trim('-');
        // An id that had to change in any way (a character replaced, an upper-case letter lowered) gets a short hash, so that two ids never share a name.
        if (clean.Length == 0 || clean != original)
        {
            clean = (clean.Length == 0 ? "c" : clean) + "-" + Hash(id)[..6];
        }

        return "gemini-" + clean;
    }

    /// <summary>Line 0 of a conversation: its title (may be empty), where it comes from and its id. It is a line, and takes position 0.</summary>
    public static string Meta(string id, string? title, string source) =>
        JsonSerializer.Serialize(new { v = 1, meta = new { title = Cut(title), source, id } }, Json);

    /// <summary>
    /// The longest text of a turn that is sent: Mnemarr keeps 20 000 characters and marks the cut, and a line over 1.5 MB is refused whole (SPEC §5.2), which would hold back the rest of its conversation
    /// for ever. A turn is cut well above what Mnemarr keeps, so that Mnemarr still marks the cut itself.
    /// </summary>
    public const int MaxTurnCharacters = 100_000;

    public static string TurnLine(Turn turn) =>
        JsonSerializer.Serialize(new { v = 1, role = turn.Role, at = turn.At?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture), text = CutTurn(turn.Text) }, Json);

    private static string CutTurn(string text)
    {
        if (text.Length <= MaxTurnCharacters)
        {
            return text;
        }

        // Not in the middle of a pair of characters that make one.
        var end = char.IsHighSurrogate(text[MaxTurnCharacters - 1]) ? MaxTurnCharacters - 1 : MaxTurnCharacters;
        return text[..end];
    }

    /// <summary>A short hash of a title, to see that it changed without keeping it (a title is made of what the person asked).</summary>
    public static string? TitleHashOf(string? title) => string.IsNullOrWhiteSpace(title) ? null : Hash(title.Trim())[..16];

    /// <summary>A short hash of a turn, to see that a turn that was sent has been rewritten.</summary>
    public static string HashOf(Turn turn) => Hash(turn.Role + "\n" + turn.Text)[..16];

    /// <summary>Cuts lines into batches Mnemarr takes: at most 500 lines and 3 MB, each with the position of its first line.</summary>
    public static IReadOnlyList<(long From, IReadOnlyList<string> Lines)> Batches(long from, IReadOnlyList<string> lines)
    {
        var batches = new List<(long, IReadOnlyList<string>)>();
        var current = new List<string>();
        var bytes = 0;
        var start = from;
        foreach (var line in lines)
        {
            var size = Encoding.UTF8.GetByteCount(line);
            if (current.Count > 0 && (current.Count >= MaxLinesPerBatch || bytes + size > MaxBytesPerBatch))
            {
                batches.Add((start, current));
                start += current.Count;
                current = [];
                bytes = 0;
            }

            current.Add(line);
            bytes += size;
        }

        if (current.Count > 0)
        {
            batches.Add((start, current));
        }

        return batches;
    }

    private static string? Cut(string? title)
    {
        var text = title?.Trim();
        return text is { Length: > MaxTitleLength } ? text[..MaxTitleLength] : text;
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
