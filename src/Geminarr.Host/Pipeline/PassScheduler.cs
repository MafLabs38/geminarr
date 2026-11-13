using Geminarr.Core.Connection;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Storage;
using Microsoft.Extensions.Options;

namespace Geminarr.Host.Pipeline;

/// <summary>
/// Runs a pass every few minutes (SPEC §3.3, §6.4): the interval is the setting, a random 20 % either way, doubled for each pass that failed in a row up to a limit, and shortened
/// by "run now". A failure of the database or of Mnemarr is logged and waited out: the container stays up and says what is wrong through its health.
/// </summary>
public sealed class PassScheduler : BackgroundService
{
    /// <summary>A short wait at start, so that the application answers its probes before the first pass.</summary>
    private static readonly TimeSpan DefaultStartDelay = TimeSpan.FromSeconds(3);

    private readonly PassRunner _runner;
    private readonly PassTrigger _trigger;
    private readonly LinkService _link;
    private readonly ConnectionTracker _connection;
    private readonly IOptions<ReaderOptions> _options;
    private readonly ILogger<PassScheduler> _logger;
    private readonly TimeSpan _startDelay;

    public PassScheduler(PassRunner runner, PassTrigger trigger, LinkService link, ConnectionTracker connection, IOptions<ReaderOptions> options, ILogger<PassScheduler> logger, TimeSpan? startDelay = null)
    {
        _startDelay = startDelay ?? DefaultStartDelay;
        _runner = runner;
        _trigger = trigger;
        _link = link;
        _connection = connection;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_startDelay, stoppingToken);
            await EnrolAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await PassAsync(stoppingToken);
                await _trigger.WaitAsync(NextDelay(), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The application is stopping.
        }
    }

    private async Task EnrolAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _link.EnrolFromCodeFileAsync(cancellationToken);
        }
        catch (SinkException ex)
        {
            _logger.LogWarning(ex, "The enrolment code was not accepted by Mnemarr ({Failure}).", ex.Failure);
        }
        catch (Exception ex) when (StorageErrors.IsUnavailable(ex))
        {
            _logger.LogWarning(ex, "The enrolment could not be kept: the database cannot be reached.");
        }
    }

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _runner.RunAsync(cancellationToken);
        }
        catch (Exception ex) when (StorageErrors.IsUnavailable(ex))
        {
            // The probes say it (the database check); the next pass tries again.
            _logger.LogWarning(ex, "A pass could not run: the database cannot be reached.");
        }
    }

    private TimeSpan NextDelay()
    {
        var options = _options.Value;
        var failures = 0;
        try
        {
            failures = _connection.Current().ConsecutiveFailures;
        }
        catch (Exception ex) when (StorageErrors.IsUnavailable(ex))
        {
            failures = 1;
        }

        return PassSchedule.NextDelay(options.PollMinutes, failures, options.MaxBackoffMinutes, Random.Shared.NextDouble());
    }
}
