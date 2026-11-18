namespace Geminarr.Core.Extension;

/// <summary>What the extension says, in one shape with a kind: <c>state</c> (signed in or not), <c>list</c> (the conversations of the page, most recent first) or <c>conversation</c> (the turns of one).</summary>
public sealed record ExtensionPushRequest(string? Kind, bool? SignedIn, string? Id, string? Title, IReadOnlyList<PushedItem>? Items, IReadOnlyList<PushedTurn>? Turns, string? Version);

public sealed record PushedItem(string? Id, string? Title);

public sealed record PushedTurn(string? Role, string? Text);

/// <summary>What Geminarr answers: whether it took it, why not in a sentence of ours, and which conversations of the list it wants the extension to read now.</summary>
public sealed record ExtensionPushResult(bool Accepted, string? Problem, IReadOnlyList<string> Open)
{
    public static ExtensionPushResult Ok(IReadOnlyList<string>? open = null) => new(true, null, open ?? []);

    public static ExtensionPushResult Refused(string problem) => new(false, problem, []);
}
