namespace Geminarr.Core.Extension;

/// <summary>What the extension says, in one shape with a kind: <c>state</c> (nobody is signed in), <c>hello</c> (it is there: it is answered with what is asked of it), <c>list</c> (the conversations of the page, most recent first), <c>conversation</c> (the turns of one) or <c>broken</c> (the page is not what it knows, at a <c>step</c>).</summary>
public sealed record ExtensionPushRequest(string? Kind, bool? SignedIn, string? Id, string? Title, IReadOnlyList<PushedItem?>? Items, IReadOnlyList<PushedTurn?>? Turns, string? Version, string? Step = null, IReadOnlyList<string?>? Labels = null, bool? Complete = null);

public sealed record PushedItem(string? Id, string? Title);

public sealed record PushedTurn(string? Role, string? Text);

/// <summary>
/// What Geminarr answers: whether it took it, why not in a sentence of ours, and which conversations of the list it wants the extension to read now (<see cref="Anchored"/>: those of them whose end is enough, because
/// Geminarr knows the rest; <see cref="More"/>: the end that was handed over does not join what Geminarr holds, so the whole conversation is wanted).
/// </summary>
public sealed record ExtensionPushResult(bool Accepted, string? Problem, IReadOnlyList<string> Open, bool Round = false, bool SignOut = false, IReadOnlyList<string>? Anchored = null, bool More = false)
{
    public static ExtensionPushResult Ok(IReadOnlyList<string>? open = null, IReadOnlyList<string>? anchored = null) => new(true, null, open ?? [], Anchored: anchored ?? []);

    /// <summary>The end of a conversation that was handed over does not join what Geminarr holds of it: the extension is asked for the whole of it.</summary>
    public static ExtensionPushResult NeedsMore() => new(true, null, [], More: true);

    /// <summary>What a heartbeat is answered: whether the extension is asked for a round now (a person is signing in, the operator asked to look again, nothing is known yet) and whether it is asked to sign the account out.</summary>
    public static ExtensionPushResult Hints(bool round, bool signOut) => new(true, null, [], round, signOut);

    public static ExtensionPushResult Refused(string problem) => new(false, problem, []);
}
