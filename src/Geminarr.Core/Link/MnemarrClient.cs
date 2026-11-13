using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Link;

/// <summary>What Mnemarr answers when a machine asks to be paired: the code to show, the secret to ask with, the page where a person answers, and how long and how often.</summary>
public sealed record PairStarted(string UserCode, string DeviceCode, string VerificationUrl, int ExpiresInSeconds, int IntervalSeconds);

/// <summary>The answer so far to a pairing: <c>pending</c>, <c>approved</c> (with the key, once), <c>rejected</c> or <c>expired</c>.</summary>
public sealed record PairPolled(string State, string? Key, string? Machine);

/// <summary>What a machine is given when it enrols: its key (once) and the name it was made for.</summary>
public sealed record Enrolled(string Key, string Machine);

/// <summary>
/// Mnemarr's ingest API, as a machine uses it (SPEC §5; Mnemarr's ADR 0024, 0027, 0030, 0041): positions, conversations, heartbeat, and the two ways to get a key. Redirects are not
/// followed (a key must not follow a redirect to another host), every call is bounded as a whole (30 seconds, the answer included), and every failure says which kind it is (<see cref="SinkException"/>) and never what was sent.
/// </summary>
public sealed class MnemarrClient
{
    public const string HttpClientName = "mnemarr";

    /// <summary>A write that does not carry an API key (pairing, enrolment) needs this header on Mnemarr; the ones that do not need it, and it does no harm.</summary>
    private const string RequestHeader = "X-Mnemarr-Request";

    /// <summary>The largest answer that is read: a list of positions for tens of thousands of conversations fits well inside.</summary>
    private const int MaxAnswerBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A call as a whole (the answer to the last byte of it) never lasts longer than this.</summary>
    private static readonly TimeSpan DefaultCallTimeout = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _factory;
    private readonly TimeSpan _callTimeout;

    public MnemarrClient(IHttpClientFactory factory, TimeSpan? callTimeout = null)
    {
        _factory = factory;
        _callTimeout = callTimeout ?? DefaultCallTimeout;
    }

    public async Task<SinkPositions> PositionsAsync(string url, string key, string machine, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Get, $"{url}/api/v1/ingest/positions?machine={Uri.EscapeDataString(machine)}", key);
        var rows = await SendAsync<List<PositionRow>>(request, cancellationToken);
        return new SinkPositions((rows ?? []).Where(row => row.Session is not null).GroupBy(row => row.Session!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Max(row => row.Lines), StringComparer.Ordinal));
    }

    public async Task<SinkResult> SendAsync(string url, string key, string machine, string session, long from, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, $"{url}/api/v1/ingest/conversations", key, new { format = "chat-turns", machine, application = "gemini", session, from, lines });
        var result = await SendAsync<ResultRow>(request, cancellationToken);
        return new SinkResult(result?.Next ?? from + lines.Count, result?.Accepted ?? lines.Count);
    }

    public async Task HeartbeatAsync(string url, string key, Heartbeat heartbeat, CancellationToken cancellationToken)
    {
        var body = new
        {
            version = heartbeat.Version,
            pendingLines = heartbeat.PendingLines,
            error = heartbeat.Error,
            paused = false,
            attention = heartbeat.Attention is { } attention ? new { code = attention.Code, url = attention.Url } : null,
        };
        using var request = Build(HttpMethod.Post, $"{url}/api/v1/ingest/heartbeat", key, body);
        await SendAsync<object>(request, cancellationToken);
    }

    public async Task<Enrolled> EnrolAsync(string url, string code, string machine, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, $"{url}/api/v1/ingest/enrol", key: null, new { code, machine });
        var result = await SendAsync<EnrolRow>(request, cancellationToken);
        return string.IsNullOrWhiteSpace(result?.Key) ? throw new SinkException(SinkFailure.Rejected, "Mnemarr gave no key.") : new Enrolled(result.Key, result.Machine ?? machine);
    }

    public async Task<PairStarted> PairStartAsync(string url, string hostname, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, $"{url}/api/v1/ingest/pair", key: null, new { hostname });
        var result = await SendAsync<PairStarted>(request, cancellationToken);
        return result is { UserCode.Length: > 0, DeviceCode.Length: > 0 } ? result : throw new SinkException(SinkFailure.Rejected, "Mnemarr did not start a pairing.");
    }

    public async Task<PairPolled> PairPollAsync(string url, string deviceCode, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, $"{url}/api/v1/ingest/pair/poll", key: null, new { deviceCode });
        var result = await SendAsync<PairPolled>(request, cancellationToken);
        return result is { State.Length: > 0 } ? result : throw new SinkException(SinkFailure.Rejected, "Mnemarr gave no state for the pairing.");
    }

    private static HttpRequestMessage Build(HttpMethod method, string address, string? key, object? body = null)
    {
        var request = new HttpRequestMessage(method, address);
        if (key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        if (method != HttpMethod.Get)
        {
            request.Headers.Add(RequestHeader, "1");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        return request;
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken caller)
    {
        // The time of the client stops counting once the headers are there: the whole call, answer included, has its own, so that a Mnemarr that stops in the middle of an answer does not hold a pass forever.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(caller);
        limit.CancelAfter(_callTimeout);
        var cancellationToken = limit.Token;
        try
        {
            using var client = _factory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw Failure(response.StatusCode);
            }

            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            {
                return default;
            }

            // An answer is read up to a limit, counting what arrives: an endpoint that never stops must not fill the memory of the container.
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var limited = new MemoryStream();
            await CopyAtMostAsync(body, limited, MaxAnswerBytes, cancellationToken);
            limited.Position = 0;
            return await JsonSerializer.DeserializeAsync<T>(limited, Json, cancellationToken);
        }
        catch (Exception ex) when (IsFailureOfTheCall(ex, caller))
        {
            // Never the message of the exception: it can hold the address, and the address is not for a log or the page.
            throw new SinkException(SinkFailure.Unreachable, "Mnemarr did not answer, or not with an answer this version understands.");
        }
    }

    /// <summary>A failure of the call itself (the network, a timeout, an answer that is not what was expected), and not the caller giving up.</summary>
    private static bool IsFailureOfTheCall(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && exception is HttpRequestException or OperationCanceledException or IOException or JsonException or NotSupportedException;

    private static async Task CopyAtMostAsync(Stream source, Stream target, int limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[8_192];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (target.Length + read > limit)
            {
                throw new JsonException("The answer is larger than this version reads.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static SinkException Failure(HttpStatusCode status) => (int)status switch
    {
        401 or 403 => new SinkException(SinkFailure.Refused, "Mnemarr refused the key of this machine."),
        400 or 404 or 405 or 409 or 413 or 415 or 422 => new SinkException(SinkFailure.Rejected, $"Mnemarr said no ({(int)status})."),
        _ => new SinkException(SinkFailure.Unreachable, $"Mnemarr answered {(int)status}."),
    };

    private sealed record PositionRow(string? Session, long Lines);

    private sealed record ResultRow(long Next, int Accepted);

    private sealed record EnrolRow(string? Key, string? Machine);
}
