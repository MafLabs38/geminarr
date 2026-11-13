namespace Geminarr.Core.Pipeline;

/// <summary>Whether the assistant's session is good: signed in, or an explicit sign-in page (a network error is never "signed out").</summary>
public enum SessionCheck
{
    SignedIn,
    SignedOut,

    /// <summary>There is no profile yet: nobody has signed in, so there is nothing to check and nothing to ask for.</summary>
    NoProfile,
}

/// <summary>A conversation in the list of the assistant: its own id, its title if it shows one, and its rank (0 is the most recent).</summary>
public sealed record ConversationHeader(string Id, string? Title, int Rank);

/// <summary>One turn: who spoke, when if the product shows it, and the text (plain or Markdown as written, never HTML).</summary>
public sealed record Turn(string Role, DateTimeOffset? At, string Text);

/// <summary>The conversations that Mnemarr holds for the machine: the number of lines of each session (the meta line is line 0).</summary>
public sealed record SinkPositions(IReadOnlyDictionary<string, long> Lines);

/// <summary>What Mnemarr answers to a batch: where it stands now, and how many lines it took.</summary>
public sealed record SinkResult(long Next, int Accepted);

/// <summary>Why a reader could not do its work: a code from <c>ConnectionReasons</c> and a sentence with no path and no content.</summary>
public sealed class ReaderException : Exception
{
    public ReaderException(string reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public string Reason { get; }
}
