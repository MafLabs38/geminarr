using Geminarr.Core.Pipeline;

namespace Geminarr.Integration.Test.Support;

/// <summary>A reader that holds the pass at the check of the session until the test releases it: a pass that is running, for as long as the test needs.</summary>
public sealed class BlockingReader : IConversationReader
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Source => ConversationSources.Web;

    public void Release() => _release.TrySetResult();

    public async Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
        return SessionCheck.SignedIn;
    }

    public Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ConversationHeader>>([]);

    public Task<IReadOnlyList<Turn>> OpenAsync(string id, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Turn>>([]);
}
