using System.Net;
using System.Net.Http.Json;
using System.Text;
using Geminarr.Api.V1.Extension;
using Geminarr.Core.Extension;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geminarr.Integration.Test;

/// <summary>A host whose reader is the extension (ADR 0022): a token in a file, and a package folder with a signed file (its content is not checked here, only served).</summary>
public sealed class ExtensionFactory : GeminarrFactoryBase
{
    public const string Token = "token-token-token-token-token-token-token";
    public const string ExtensionId = "abcdefghijklmnopabcdefghijklmnop";
    public const string FileName = "geminarr-reader-1.2.3.crx";
    public static readonly byte[] Package = [0x43, 0x72, 0x32, 0x34, 3, 0, 0, 0, 9, 9];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"geminarr-extension-{Guid.NewGuid():N}");
    private readonly bool _withPackage;

    private readonly string _tokenText;
    private readonly string _readerKind;

    public ExtensionFactory(bool withPackage = true, string tokenText = Token, string readerKind = "Web") : base(createFolder: true)
    {
        _withPackage = withPackage;
        _tokenText = tokenText;
        _readerKind = readerKind;
    }

    protected override void AddSettings(Dictionary<string, string?> settings)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "token"), _tokenText + "\n");
        settings["Reader:Kind"] = _readerKind;
        settings["Extension:TokenFile"] = Path.Combine(_folder, "token");
        settings["Extension:PackageFolder"] = _folder;
        if (_withPackage)
        {
            File.WriteAllText(Path.Combine(_folder, "extension.json"), $$"""{ "id": "{{ExtensionId}}", "version": "1.2.3", "file": "{{FileName}}" }""");
            File.WriteAllBytes(Path.Combine(_folder, FileName), Package);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}

/// <summary>What the extension of the browser can do with the API, and what nobody else can.</summary>
public sealed class ExtensionApiFixture
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;
    private static readonly string ExtensionOrigin = $"chrome-extension://{ExtensionFactory.ExtensionId}";

    private static HttpRequestMessage Push(object body, string? token = ExtensionFactory.Token, bool header = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/extension/push") { Content = JsonContent.Create(body) };
        if (token is not null)
        {
            request.Headers.Add("Authorization", $"Bearer {token}");
        }

        if (header)
        {
            request.Headers.Add("X-Geminarr-Request", "1");
        }

        return request;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-token-0123456789abcdef0123456789")]
    [InlineData("token-token-token-token-token-token-toke")]
    public async Task should_refuse_a_push_without_the_token(string? token)
    {
        using var factory = new ExtensionFactory();

        var response = await factory.CreateClient().SendAsync(Push(new { kind = "state", signedIn = true }, token), CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_refuse_a_push_with_the_token_before_reading_a_body_that_is_not_even_json()
    {
        using var factory = new ExtensionFactory();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/extension/push") { Content = new StringContent("{ this is not json", Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Geminarr-Request", "1");

        (await factory.CreateClient().SendAsync(request, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the token is asked for before the body is looked at");
    }

    [Fact]
    public async Task should_refuse_a_token_that_is_too_short_even_when_it_is_the_one_that_is_presented()
    {
        using var factory = new ExtensionFactory(tokenText: "short-token");

        (await factory.CreateClient().SendAsync(Push(new { kind = "state", signedIn = true }, token: "short-token"), CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_say_that_the_reader_is_not_the_web_one_without_repeating_what_it_was_told()
    {
        using var factory = new ExtensionFactory(readerKind: "None");

        var response = await factory.CreateClient().SendAsync(Push(new { kind = "conversation", id = "c1", turns = new[] { new { role = "user", text = "a sentence that must not come back" } } }), CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        body.Should().Contain("GEMINARR__READER__KIND").And.NotContain("sentence");
    }

    [Fact]
    public async Task should_refuse_a_body_larger_than_a_conversation_can_be()
    {
        using var factory = new ExtensionFactory();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/extension/push") { Content = new StringContent("{\"kind\":\"state\",\"version\":\"" + new string('1', 9 * 1024 * 1024) + "\"}", Encoding.UTF8, "application/json") };
        request.Headers.Add("Authorization", $"Bearer {ExtensionFactory.Token}");
        request.Headers.Add("X-Geminarr-Request", "1");

        (await factory.CreateClient().SendAsync(request, CancellationToken)).StatusCode.Should().BeOneOf(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_answer_400_and_not_500_to_a_list_or_a_conversation_that_holds_a_null()
    {
        using var factory = new ExtensionFactory();
        using var client = factory.CreateClient();
        await client.SendAsync(Push(new { kind = "list", items = new[] { new { id = "c1", title = "One" } } }), CancellationToken);

        (await client.SendAsync(Push(new { kind = "list", items = new object?[] { null } }), CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.SendAsync(Push(new { kind = "conversation", id = "c1", turns = new object?[] { null } }), CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_take_the_word_that_the_page_is_broken()
    {
        using var factory = new ExtensionFactory();

        (await factory.CreateClient().SendAsync(Push(new { kind = "broken", step = "page" }), CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.CreateClient().SendAsync(Push(new { kind = "broken", step = "whatever the page said" }), CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_refuse_a_push_when_no_token_is_configured()
    {
        using var factory = new GeminarrFactory();

        (await factory.CreateClient().SendAsync(Push(new { kind = "state", signedIn = true }), CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_refuse_a_push_without_the_header_of_a_write_even_with_the_token()
    {
        using var factory = new ExtensionFactory();

        (await factory.CreateClient().SendAsync(Push(new { kind = "state", signedIn = true }, header: false), CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_take_the_state_a_list_and_a_conversation_and_ask_for_the_new_ones()
    {
        using var factory = new ExtensionFactory();
        using var client = factory.CreateClient();

        (await client.SendAsync(Push(new { kind = "state", signedIn = true, version = "1.2.3" }), CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await client.SendAsync(Push(new { kind = "list", items = new[] { new { id = "c1", title = "One" }, new { id = "c2", title = "Two" } } }), CancellationToken);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<ExtensionPushResponse>(CancellationToken))!.Open.Should().Equal("c1", "c2");

        var conversation = await client.SendAsync(Push(new { kind = "conversation", id = "c1", title = "One", turns = new[] { new { role = "user", text = "hi" }, new { role = "assistant", text = "hello" } } }), CancellationToken);
        conversation.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Services.GetRequiredService<ExtensionInbox>().Take("c1")!.Select(turn => (turn.Role, turn.Text)).Should().Equal(("user", "hi"), ("assistant", "hello"));
    }

    [Fact]
    public async Task should_refuse_what_is_not_a_conversation_without_telling_what_it_was_told()
    {
        using var factory = new ExtensionFactory();

        using var client = factory.CreateClient();
        await client.SendAsync(Push(new { kind = "list", items = new[] { new { id = "c1", title = "One" } } }), CancellationToken);

        var response = await client.SendAsync(Push(new { kind = "conversation", id = "c1", turns = new[] { new { role = "tool", text = "a sentence that must not come back" } } }), CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(CancellationToken)).Should().NotContain("sentence");
    }

    [Fact]
    public async Task should_answer_400_to_a_push_without_a_body_or_with_something_else()
    {
        using var factory = new ExtensionFactory();
        using var client = factory.CreateClient();
        using var empty = Push(new { });
        empty.Content = new StringContent("null", Encoding.UTF8, "application/json");

        (await client.SendAsync(empty, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.SendAsync(Push(new { kind = "something" }), CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_let_the_origin_of_the_extension_and_no_other_read_the_answer()
    {
        using var factory = new ExtensionFactory();
        using var client = factory.CreateClient();

        using var own = Push(new { kind = "state", signedIn = true });
        own.Headers.Add("Origin", ExtensionOrigin);
        var ownResponse = await client.SendAsync(own, CancellationToken);
        using var foreign = Push(new { kind = "state", signedIn = true });
        foreign.Headers.Add("Origin", "https://evil.example");
        var foreignResponse = await client.SendAsync(foreign, CancellationToken);

        ownResponse.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(ExtensionOrigin);
        foreignResponse.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task should_answer_the_preflight_of_the_extension_on_the_push_route_only()
    {
        using var factory = new ExtensionFactory();
        using var client = factory.CreateClient();

        using var preflight = Preflight("/api/v1/extension/push", ExtensionOrigin);
        preflight.Headers.Add("Access-Control-Request-Private-Network", "true");
        var answer = await client.SendAsync(preflight, CancellationToken);
        var other = await client.SendAsync(Preflight("/api/v1/runs", ExtensionOrigin), CancellationToken);
        var foreign = await client.SendAsync(Preflight("/api/v1/extension/push", "https://evil.example"), CancellationToken);

        answer.StatusCode.Should().Be(HttpStatusCode.NoContent);
        answer.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(ExtensionOrigin);
        answer.Headers.GetValues("Access-Control-Allow-Methods").Should().Equal("POST");
        answer.Headers.GetValues("Access-Control-Allow-Headers").Single().Should().Contain("authorization").And.Contain("x-geminarr-request");
        answer.Headers.GetValues("Access-Control-Allow-Private-Network").Should().Equal("true");
        other.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        foreign.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Theory]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop/")]
    [InlineData("chrome-extension://ABCDEFGHIJKLMNOPABCDEFGHIJKLMNOP")]
    [InlineData("http://abcdefghijklmnopabcdefghijklmnop")]
    [InlineData("null")]
    [InlineData("")]
    public async Task should_give_no_header_to_an_origin_that_is_not_exactly_the_one_of_the_extension(string origin)
    {
        using var factory = new ExtensionFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Preflight("/api/v1/extension/push", origin), CancellationToken);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        response.Headers.GetValues("Vary").Should().Contain("Origin", "the answer depends on the origin whatever it is");
    }

    [Fact]
    public async Task should_not_look_at_the_package_for_a_request_that_is_not_for_the_push_route()
    {
        using var factory = new ExtensionFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Vary").Should().BeFalse("the route of the push is the only one that speaks of origins");
    }

    private static HttpRequestMessage Preflight(string path, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return request;
    }

    [Fact]
    public async Task should_serve_the_update_manifest_and_the_package_to_a_browser_with_no_session()
    {
        using var factory = new ExtensionFactory();
        using var client = factory.CreateClient();

        var manifest = await client.GetAsync("/api/v1/extension/update.xml", CancellationToken);
        var xml = await manifest.Content.ReadAsStringAsync(CancellationToken);
        var package = await client.GetAsync($"/api/v1/extension/package/{ExtensionFactory.FileName}", CancellationToken);

        manifest.StatusCode.Should().Be(HttpStatusCode.OK);
        manifest.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
        xml.Should().Contain($"appid='{ExtensionFactory.ExtensionId}'").And.Contain("version='1.2.3'").And.Contain($"codebase='http://localhost/api/v1/extension/package/{ExtensionFactory.FileName}'");
        package.StatusCode.Should().Be(HttpStatusCode.OK);
        package.Content.Headers.ContentType!.MediaType.Should().Be("application/x-chrome-extension");
        (await package.Content.ReadAsByteArrayAsync(CancellationToken)).Should().Equal(ExtensionFactory.Package);
    }

    [Theory]
    [InlineData("other.crx")]
    [InlineData("..%2Ftoken")]
    [InlineData("extension.json")]
    public async Task should_serve_no_file_but_the_package_the_description_names(string name)
    {
        using var factory = new ExtensionFactory();

        (await factory.CreateClient().GetAsync($"/api/v1/extension/package/{name}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_say_not_found_when_the_image_carries_no_package()
    {
        using var factory = new ExtensionFactory(withPackage: false);
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/extension/update.xml", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/extension/package/{ExtensionFactory.FileName}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var own = Push(new { kind = "state", signedIn = true });
        own.Headers.Add("Origin", ExtensionOrigin);
        (await client.SendAsync(own, CancellationToken)).Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse("without a package nobody knows the origin of the extension");
    }

    [Fact]
    public async Task should_name_the_package_by_the_public_address_when_one_is_given()
    {
        using var factory = new ExtensionFactory();
        using var scoped = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Extension:PublicUrl"] = "https://geminarr.homelab.example/" })));

        var xml = await scoped.CreateClient().GetStringAsync("/api/v1/extension/update.xml", CancellationToken);

        xml.Should().Contain($"codebase='https://geminarr.homelab.example/api/v1/extension/package/{ExtensionFactory.FileName}'");
    }
}
