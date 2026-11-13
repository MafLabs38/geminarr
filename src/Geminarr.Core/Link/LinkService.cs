using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Link;

/// <summary>
/// The link to Mnemarr (SPEC §5.1): where to send and as which machine, how a key is obtained (pairing by asking, enrolment with a code, a key file that was given), what is
/// wrong with the destination, and the way to unlink. The address comes from the settings when they give it, from the page otherwise; the key is a file and nothing else.
/// </summary>
public sealed class LinkService
{
    /// <summary>A pairing lives at least this long and at most this long, whatever Mnemarr says.</summary>
    private const int MinPairingSeconds = 30;

    private const int MaxPairingSeconds = 3_600;

    private readonly IOptions<MnemarrOptions> _options;
    private readonly IDestinationStore _store;
    private readonly IKeyStore _keys;
    private readonly MnemarrClient _client;
    private readonly TimeProvider _clock;
    private readonly ILogger<LinkService> _logger;
    private readonly IOutboxStore _outbox;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _polling = new(1, 1);
    private PendingPairing? _pairing;
    private long _generation;

    public LinkService(IOptions<MnemarrOptions> options, IDestinationStore store, IKeyStore keys, MnemarrClient client, TimeProvider clock, ILogger<LinkService> logger, IOutboxStore outbox)
    {
        _outbox = outbox;
        _options = options;
        _store = store;
        _keys = keys;
        _client = client;
        _clock = clock;
        _logger = logger;
    }

    private string? SettingsUrl => MnemarrAddress.Normalize(_options.Value.Url);

    /// <summary>Changes each time the destination does (an address, a link, an unlink): what was asked of the Mnemarr before is not the truth about the next.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    public DestinationStatus Status()
    {
        var row = _store.Get();
        var url = SettingsUrl ?? row?.Url;
        var hasKey = _keys.Read() is not null && KeyBelongsTo(row, url);
        var problem = ProblemOf(url, hasKey, row?.LastProblem);
        return new DestinationStatus(url, row?.Machine ?? _options.Value.Machine, hasKey && url is not null, SettingsUrl is not null, !string.IsNullOrWhiteSpace(_options.Value.KeyFile), problem, row?.LastHeartbeatAt);
    }

    /// <summary>Where to send, as which machine, with which key; a <see cref="SinkException"/> says why there is nowhere to send.</summary>
    public ResolvedDestination Resolve()
    {
        var row = _store.Get();
        var url = SettingsUrl ?? row?.Url;
        var key = KeyBelongsTo(row, url) ? _keys.Read() : null;
        return ProblemOf(url, key is not null, problem: null) switch
        {
            DestinationProblems.NotLinked => throw new SinkException(SinkFailure.NotLinked, "No Mnemarr is linked."),
            DestinationProblems.InsecureAddress => throw new SinkException(SinkFailure.InsecureAddress, "The address of Mnemarr is not https."),
            _ => new ResolvedDestination(url!, row?.Machine ?? _options.Value.Machine, key!),
        };
    }

    /// <summary>
    /// A key that a pairing or an enrolment gave belongs to the Mnemarr that gave it: it is never sent to another address, whoever changed the address (the settings included). A key that was given as a
    /// file by the deployment is the deployment's to bind to its address.
    /// </summary>
    private bool KeyBelongsTo(DestinationRecord? row, string? url) =>
        (_keys.AddressOf() ?? (row is { LinkedAt: not null } ? row.Url : null)) is not { } bound || string.Equals(bound, url, StringComparison.OrdinalIgnoreCase);

    /// <summary>The code of what is wrong with the destination, or null. A problem the last talk left (refused, unreachable) is kept until the next one works.</summary>
    private string? ProblemOf(string? url, bool hasKey, string? problem)
    {
        if (url is null || !hasKey)
        {
            return DestinationProblems.NotLinked;
        }

        return !MnemarrAddress.IsSecure(url) && !_options.Value.AllowInsecureHttp ? DestinationProblems.InsecureAddress : problem;
    }

    /// <summary>The address, given on the page. Refused when the settings give it, or when a key is held (it belongs to the Mnemarr that gave it: unlink first).</summary>
    public void SetAddress(string address)
    {
        if (SettingsUrl is not null)
        {
            throw new InvalidOperationException("The address of Mnemarr is set by the deployment.");
        }

        if (_keys.Read() is not null)
        {
            throw new InvalidOperationException("Unlink before changing the address: the key belongs to the Mnemarr that gave it.");
        }

        _store.SaveAddress(MnemarrAddress.Normalize(address) ?? throw new ArgumentException("Not an address of Mnemarr.", nameof(address)));
        Interlocked.Increment(ref _generation);
    }

    /// <summary>Starts a pairing by asking (ADR 0030): Mnemarr gives a code and a page where a person approves it.</summary>
    public async Task<PairingStatus> StartPairingAsync(CancellationToken cancellationToken)
    {
        var url = SettingsUrl ?? _store.Get()?.Url ?? throw new SinkException(SinkFailure.NotLinked, "Give the address of Mnemarr first.");
        if (!MnemarrAddress.IsSecure(url) && !_options.Value.AllowInsecureHttp)
        {
            throw new SinkException(SinkFailure.InsecureAddress, "The address of Mnemarr is not https.");
        }

        _store.EnsureAddress(url);
        var started = await _client.PairStartAsync(url, _options.Value.Machine, cancellationToken);
        var pairing = new PendingPairing(url, started.DeviceCode, started.UserCode, SafeLink(started.VerificationUrl, url), _clock.GetUtcNow() + TimeSpan.FromSeconds(Math.Clamp(started.ExpiresInSeconds, MinPairingSeconds, MaxPairingSeconds)));
        lock (_gate)
        {
            _pairing = pairing;
        }

        return pairing.ToStatus(PairingStates.Pending);
    }

    public PairingStatus Pairing()
    {
        lock (_gate)
        {
            return _pairing is { } pairing && pairing.ExpiresAt > _clock.GetUtcNow() ? pairing.ToStatus(PairingStates.Pending) : new PairingStatus(PairingStates.None, null, null, null);
        }
    }

    /// <summary>Asks Mnemarr what became of the pairing. When a person approved it, the key is written and the machine is linked.</summary>
    public async Task<PairingStatus> PollPairingAsync(CancellationToken cancellationToken)
    {
        // One question at a time: Mnemarr gives the key once, and a second asker that comes while the first is waiting must hear the same answer, not race it.
        await _polling.WaitAsync(cancellationToken);
        try
        {
            return await PollOnceAsync(cancellationToken);
        }
        finally
        {
            _polling.Release();
        }
    }

    private async Task<PairingStatus> PollOnceAsync(CancellationToken cancellationToken)
    {
        PendingPairing? pairing;
        lock (_gate)
        {
            pairing = _pairing;
        }

        if (pairing is null)
        {
            return new PairingStatus(PairingStates.None, null, null, null);
        }

        if (pairing.ExpiresAt <= _clock.GetUtcNow())
        {
            Forget(pairing);
            return new PairingStatus(PairingStates.Expired, null, null, null);
        }

        var polled = await _client.PairPollAsync(pairing.Url, pairing.DeviceCode, cancellationToken);
        switch (polled.State)
        {
            case PairingStates.Approved when !string.IsNullOrWhiteSpace(polled.Key):
                // Given up (or unlinked) while Mnemarr was answering: the key is not kept. The check and the link are one step under the lock, so that an unlink cannot fall between them.
                var machine = MachineOf(polled.Machine);
                lock (_gate)
                {
                    if (!ReferenceEquals(_pairing, pairing))
                    {
                        return new PairingStatus(PairingStates.None, null, null, null);
                    }

                    Link(pairing.Url, polled.Key, machine);
                    _pairing = null;
                }

                _logger.LogInformation("Linked to Mnemarr by pairing, as the machine {Machine}.", machine);
                return new PairingStatus(PairingStates.Approved, null, null, null);
            case PairingStates.Rejected or PairingStates.Expired:
                Forget(pairing);
                return new PairingStatus(polled.State, null, null, null);
            default:
                return pairing.ToStatus(PairingStates.Pending);
        }
    }

    public void CancelPairing()
    {
        lock (_gate)
        {
            _pairing = null;
        }
    }

    /// <summary>For a deployment as code: a one-time code in a file is exchanged for the key, when there is no key yet. A key that was given as a file is used as it is.</summary>
    public async Task EnrolFromCodeFileAsync(CancellationToken cancellationToken)
    {
        var file = _options.Value.EnrolmentCodeFile;
        var url = SettingsUrl ?? _store.Get()?.Url;
        if (string.IsNullOrWhiteSpace(file) || url is null || _keys.Read() is not null || (!MnemarrAddress.IsSecure(url) && !_options.Value.AllowInsecureHttp))
        {
            return;
        }

        string code;
        try
        {
            code = (await File.ReadAllTextAsync(file, cancellationToken)).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return;
        }

        if (code.Length == 0)
        {
            return;
        }

        var enrolled = await _client.EnrolAsync(url, code, _options.Value.Machine, cancellationToken);
        var machine = MachineOf(enrolled.Machine);
        Link(url, enrolled.Key, machine);
        _logger.LogInformation("Linked to Mnemarr by enrolment, as the machine {Machine}.", machine);
    }

    /// <summary>Forgets the key and the machine, and any pairing: Geminarr no longer sends. What Mnemarr already holds stays there.</summary>
    public void Unlink()
    {
        CancelPairing();
        _keys.Delete();
        _store.Clear();
        // What waits for the Mnemarr that is left is text of conversations: it does not stay in the database with nobody to acknowledge it.
        _outbox.Clear();
        Interlocked.Increment(ref _generation);
        if (_keys.Read() is not null)
        {
            throw new InvalidOperationException("The key is a file of the deployment and could not be removed: take it out of the manifest.");
        }
    }

    /// <summary>The result of the last talk with Mnemarr: when, and the code of the problem (null when it worked).</summary>
    public void RecordTalk(string? problem)
    {
        if (SettingsUrl is { } url)
        {
            _store.EnsureAddress(url);
        }

        _store.RecordHeartbeat(_clock.GetUtcNow(), problem);
    }

    private void Link(string url, string key, string machine)
    {
        _keys.Write(key, url);
        _store.EnsureAddress(url);
        _store.SaveLinked(machine, _clock.GetUtcNow());
        Interlocked.Increment(ref _generation);
    }

    /// <summary>The name Mnemarr gave the machine, if it is a name this version accepts (what Mnemarr says is checked like anything that comes from outside); the configured one otherwise.</summary>
    private string MachineOf(string? given) =>
        given is { Length: > 0 and <= 40 } && given.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-') ? given : _options.Value.Machine;

    private void Forget(PendingPairing pairing)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_pairing, pairing))
            {
                _pairing = null;
            }
        }
    }

    /// <summary>The page where a person approves is Mnemarr's own, and shown to the operator with its host visible; anything that is not an http or https address is not shown.</summary>
    private static string? SafeLink(string? address, string mnemarrUrl) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.UserInfo.Length == 0
        && (uri.Scheme == Uri.UriSchemeHttps || mnemarrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ? uri.ToString()
            : null;

    private sealed record PendingPairing(string Url, string DeviceCode, string UserCode, string? VerificationUrl, DateTimeOffset ExpiresAt)
    {
        public PairingStatus ToStatus(string state) => new(state, UserCode, VerificationUrl, ExpiresAt);
    }
}
