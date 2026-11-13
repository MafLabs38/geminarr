using System.Net;
using System.Text;
using System.Text.Json;

namespace Geminarr.Integration.Test.Support;

/// <summary>
/// A Mnemarr in memory, as far as a machine sees it (its ingest API: pairing, enrolment, positions, conversations, heartbeat). It holds what it is sent like the real one: it skips an overlap and refuses a hole,
/// and it asks for the key of the machine. The real one is tried, by hand, in the sandbox; this one keeps the tests fast and free of the network.
/// </summary>
public sealed class FakeMnemarr : HttpMessageHandler
{
    public const string Key = "mnm_fake-key";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();

    /// <summary>Whether a person approved the pairing: the next poll gives the key.</summary>
    public bool Approved { get; set; }

    /// <summary>The name a person gave the machine when they approved the pairing.</summary>
    public string MachineName { get; set; } = "gemini";

    /// <summary>Makes every call fail with this status (unauthorized, server error…) until it is null.</summary>
    public HttpStatusCode? FailWith { get; set; }

    public Dictionary<string, List<string>> Conversations { get; } = new(StringComparer.Ordinal);

    public List<JsonElement> Heartbeats { get; } = [];

    public List<string> Calls { get; } = [];

    public List<IReadOnlyDictionary<string, string>> Headers { get; } = [];

    public int Enrolments { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate)
        {
            Calls.Add($"{request.Method} {path}");
            Headers.Add(request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase));
            return FailWith is { } failure ? new HttpResponseMessage(failure) : Answer(request, path, body);
        }
    }

    private HttpResponseMessage Answer(HttpRequestMessage request, string path, string body)
    {
        var anonymous = path is "/api/v1/ingest/pair" or "/api/v1/ingest/pair/poll" or "/api/v1/ingest/enrol";
        if (anonymous)
        {
            return request.Headers.Contains("X-Mnemarr-Request") ? Anonymous(path, body) : new HttpResponseMessage(HttpStatusCode.Forbidden);
        }

        if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: Key })
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        return path switch
        {
            "/api/v1/ingest/positions" => Json(Conversations.Select(pair => new { session = pair.Key, lines = pair.Value.Count })),
            "/api/v1/ingest/conversations" => Ingest(body),
            "/api/v1/ingest/heartbeat" => Beat(body),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private HttpResponseMessage Anonymous(string path, string body)
    {
        switch (path)
        {
            case "/api/v1/ingest/pair":
                return Json(new { userCode = "ABCD-EFGH", deviceCode = "device-secret", verificationUrl = "https://mnemarr.test/link?code=ABCD-EFGH", expiresInSeconds = 600, intervalSeconds = 3 }, HttpStatusCode.Created);
            case "/api/v1/ingest/pair/poll":
                return Approved ? Json(new { state = "approved", key = Key, machine = MachineName, kind = "satellite" }) : Json(new { state = "pending", key = (string?)null, machine = (string?)null, kind = (string?)null });
            default:
                Enrolments++;
                return body.Contains("GOOD-CODE", StringComparison.Ordinal) ? Json(new { key = Key, machine = MachineName, kind = "satellite" }) : new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
    }

    private HttpResponseMessage Ingest(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != "chat-turns")
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        var session = root.GetProperty("session").GetString()!;
        var from = root.GetProperty("from").GetInt64();
        var lines = root.GetProperty("lines").EnumerateArray().Select(line => line.GetString()!).ToList();
        var held = Conversations.TryGetValue(session, out var existing) ? existing : [];
        if (from > held.Count)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        var before = held.Count;
        held.AddRange(lines.Skip((int)(held.Count - from)));
        Conversations[session] = held;
        return Json(new { next = held.Count, accepted = held.Count - before });
    }

    private HttpResponseMessage Beat(string body)
    {
        using var document = JsonDocument.Parse(body);
        Heartbeats.Add(document.RootElement.Clone());
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value, Web), Encoding.UTF8, "application/json") };
}
