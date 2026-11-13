namespace Geminarr.Core.Pipeline;

/// <summary>"Run now": the page asks, the scheduler hears it and runs a pass at once instead of waiting for the next one.</summary>
public sealed class PassTrigger : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0);

    /// <summary>Asks for a pass now. Asking twice before the scheduler wakes is the same as asking once.</summary>
    public void Request()
    {
        if (_signal.CurrentCount == 0)
        {
            _signal.Release();
        }
    }

    /// <summary>Waits until a pass is asked for or the delay has passed; true when it was asked for.</summary>
    public Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => _signal.WaitAsync(delay, cancellationToken);

    public void Dispose() => _signal.Dispose();
}
