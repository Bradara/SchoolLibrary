namespace SchoolLibrary.Services.CodeRunning;

/// <summary>
/// Wakes the background checker when code answers are waiting (an exam was submitted), so it does not have to
/// wait for its next regular look. Signals sent while it is busy are merged into one.
/// </summary>
public sealed class CodeGradingSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    /// <summary>Tells the checker that there is work.</summary>
    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled and not yet picked up: one wake-up is enough
        }
    }

    /// <summary>Waits until <see cref="Notify"/> is called or <paramref name="timeout"/> passes; true when notified.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => _signal.WaitAsync(timeout, cancellationToken);
}
