using System.Net;
using System.Text;

namespace Geminarr.Core.Test.Support;

/// <summary>A request as the fake saw it.</summary>
public sealed record SeenRequest(HttpMethod Method, string Address, IReadOnlyDictionary<string, string> Headers, string Body);

/// <summary>An HTTP client factory whose client answers from a function and remembers what it was asked: Mnemarr is never reached in a unit test.</summary>
public sealed class FakeHttp : IHttpClientFactory
{
    public FakeHttp(Func<SeenRequest, HttpResponseMessage> answer)
    {
        Answer = answer;
    }

    /// <summary>What Mnemarr answers; a test can change it between two calls.</summary>
    public Func<SeenRequest, HttpResponseMessage> Answer { get; set; }

    public List<SeenRequest> Requests { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    public HttpClient CreateClient(string name) => new(new Handler(this), disposeHandler: true);

    private sealed class Handler : HttpMessageHandler
    {
        private readonly FakeHttp _owner;

        public Handler(FakeHttp owner)
        {
            _owner = owner;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var seen = new SeenRequest(request.Method, request.RequestUri!.AbsoluteUri, headers, body);
            _owner.Requests.Add(seen);
            return _owner.Answer(seen);
        }
    }
}
