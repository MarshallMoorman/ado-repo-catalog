namespace AdoRepoCatalog.Infrastructure;

public interface IAsyncDelay
{
    Task Delay(TimeSpan duration, CancellationToken cancellationToken = default);
}

public sealed class SystemAsyncDelay : IAsyncDelay
{
    public static SystemAsyncDelay Instance { get; } = new();

    public Task Delay(TimeSpan duration, CancellationToken cancellationToken = default)
        => duration <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(duration, cancellationToken);
}

public sealed class RecordingAsyncDelay : IAsyncDelay
{
    private readonly object _sync = new();

    public List<TimeSpan> Delays { get; } = [];

    public Task Delay(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Delays.Add(duration);
        }

        return Task.CompletedTask;
    }
}
