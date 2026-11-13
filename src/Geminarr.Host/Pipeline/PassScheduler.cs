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

    /// <summary>How often a pairing that waits is asked about (Mnemarr asks for about three seconds).</summary>
    private static readonly TimeSpan PairingInterval = TimeSpan.FromSeconds(3);

    private readonly PassRunner _runner;
    private readonly PassTrigger _trigger;
    private readonly LinkService _link;
    private readonly ConnectionTracker _connection;
    private readonly IOptions<ReaderOptions> _options;
    private readonly ILogger<PassScheduler> _logger;
    private readonly TimeSpan _startDelay;
    private readonly TimeProvider _clock;

    public PassScheduler(PassRunner runner, PassTrigger trigger, LinkService link, ConnectionTracker connection, IOptions<ReaderOptions> options, ILogger<PassScheduler> logger, TimeProvider clock, TimeSpan? startDelay = null)
    {
        _clock = clock;
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
            await Task.WhenAll(PassesAsync(stoppingToken), PairingAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // The application is stopping.
        }
    }

    /// <summary>
    /// A pass at every poll interval, which sends what waits and tells Mnemarr it is alive. Reading waits longer after a pass that failed (doubled up to the limit): that backoff is the reader's, and a machine
    /// that is backing off must not go silent in Mnemarr.
    /// </summary>
    private async Task PassesAsync(CancellationToken stoppingToken)
    {
        var nextRead = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var reading = _clock.GetUtcNow() >= nextRead;
            var result = await PassAsync(reading, stoppingToken);
            if (reading)
            {
                nextRead = _clock.GetUtcNow() + ReadDelay(result);
            }

            // A pass that was asked for reads, whatever the reader's backoff says: the operator wants to see it now.
            if (await _trigger.WaitAsync(Interval(), stoppingToken))
            {
                nextRead = DateTimeOffset.MinValue;
            }
        }
    }

    /// <summary>A pairing that waits is asked about every few seconds by the server, so that it completes when a person approves it even if the page was closed.</summary>
    private async Task PairingAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(PairingInterval, stoppingToken);
            await CheckPairingAsync(stoppingToken);
        }
    }

    private async Task CheckPairingAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_link.Pairing().State == PairingStates.Pending)
            {
                await _link.PollPairingAsync(cancellationToken);
            }
        }
        catch (SinkException ex)
        {
            _logger.LogWarning(ex, "Mnemarr could not be asked about the pairing ({Failure}).", ex.Failure);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The key could not be written, the database cannot be reached: it is said, and the loop goes on (an error here must not end the host).
            _logger.LogWarning(ex, "The pairing could not be completed.");
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

    private async Task<PassResult?> PassAsync(bool reading, CancellationToken cancellationToken)
    {
        try
        {
            return await _runner.RunAsync(cancellationToken, reading);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The probes say what is wrong (the database check); the next pass tries again, and an error that nobody foresaw does not stop the application.
            _logger.LogWarning(ex, "A pass could not run.");
            return null;
        }
    }

    /// <summary>The interval between two passes: the poll interval, with its random twenty percent.</summary>
    private TimeSpan Interval() => PassSchedule.NextDelay(_options.Value.PollMinutes, failuresInARow: 0, _options.Value.MaxBackoffMinutes, Random.Shared.NextDouble());

    /// <summary>How long reading waits after this pass: the interval, doubled for each pass that failed in a row up to the limit.</summary>
    private TimeSpan ReadDelay(PassResult? result)
    {
        var failures = 0;
        if (result is null)
        {
            failures = 1;
        }
        else
        {
            try
            {
                failures = _connection.Current().ConsecutiveFailures;
            }
            catch (Exception ex) when (StorageErrors.IsUnavailable(ex))
            {
                failures = 1;
            }
        }

        return PassSchedule.NextDelay(_options.Value.PollMinutes, failures, _options.Value.MaxBackoffMinutes, Random.Shared.NextDouble());
    }
}
