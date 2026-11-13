using System.Net;
using System.Net.Http.Json;
using Geminarr.Api.V1.Identity;
using Geminarr.Core.SystemInfo;

namespace Geminarr.Integration.Test;

/// <summary>The identity of the environment: a name, a logo and the version, served to the operator who is signed in and cached like an icon.</summary>
public sealed class IdentityFixture : IClassFixture<GeminarrFactory>, IClassFixture<IdentityFactory>
{
    private readonly GeminarrFactory _default;
    private readonly IdentityFactory _deployed;

    public IdentityFixture(GeminarrFactory defaults, IdentityFactory deployed)
    {
        _default = defaults;
        _deployed = deployed;
    }

    [Theory]
    [InlineData("/api/v1/identity")]
    [InlineData("/api/v1/identity/logo")]
    public async Task should_be_for_the_operator_who_is_signed_in(string path)
    {
        var response = await _deployed.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void should_not_start_with_a_name_that_is_too_long_and_say_which_setting()
    {
        using var factory = new BadIdentityFactory();

        var start = () => factory.CreateClient();

        start.Should().Throw<Exception>().Where(exception => exception.ToString().Contains("GEMINARR__IDENTITY__"));
    }

    [Fact]
    public async Task should_be_the_product_own_when_the_deployment_says_nothing()
    {
        using var client = await _default.SignedInClientAsync();

        var identity = await client.GetFromJsonAsync<IdentityResource>("/api/v1/identity", TestContext.Current.CancellationToken);

        identity.Should().BeEquivalentTo(new IdentityResource("Geminarr", null, BuildInfo.Version, null, new IdentityColorsResource("accent", "accent")));
        (await client.GetAsync("/api/v1/identity/logo", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_say_what_the_deployment_says_and_where_the_logo_is_without_a_path()
    {
        using var client = await _deployed.SignedInClientAsync();

        var identity = await client.GetFromJsonAsync<IdentityResource>("/api/v1/identity", TestContext.Current.CancellationToken);

        identity!.Name.Should().Be("Lab");
        identity.Colors.Should().Be(new IdentityColorsResource("#3ee0c5", "inkMuted"));
        identity.LogoUrl.Should().MatchRegex("^/api/v1/identity/logo\\?v=[0-9a-f]{64}$");
        identity.LogoProblem.Should().BeNull();
    }

    [Fact]
    public async Task should_serve_the_logo_like_an_icon_that_nothing_can_run_and_that_is_kept_for_a_year()
    {
        using var client = await _deployed.SignedInClientAsync();
        var identity = await client.GetFromJsonAsync<IdentityResource>("/api/v1/identity", TestContext.Current.CancellationToken);

        var response = await client.GetAsync(identity!.LogoUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(IdentityFactory.Png);
        response.Headers.CacheControl!.Private.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
        response.Headers.CacheControl.Extensions.Should().ContainSingle(extension => extension.Name == "immutable");
        response.Headers.GetValues("Content-Security-Policy").Should().Equal("default-src 'none'; sandbox");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
    }

    [Fact]
    public async Task should_keep_the_logo_five_minutes_when_the_address_has_no_hash()
    {
        using var client = await _deployed.SignedInClientAsync();

        var response = await client.GetAsync("/api/v1/identity/logo", TestContext.Current.CancellationToken);

        response.Headers.CacheControl!.Private.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromMinutes(5));
        response.Headers.CacheControl.Extensions.Should().BeEmpty();
    }

    [Fact]
    public async Task should_cache_nothing_and_keep_the_application_policy_when_there_is_no_logo()
    {
        using var client = await _default.SignedInClientAsync();

        var response = await client.GetAsync("/api/v1/identity/logo", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("Content-Security-Policy").Single().Should().StartWith("default-src 'self'");
    }

    [Theory]
    [InlineData("v=not-the-hash")]
    [InlineData("")]
    [InlineData("the-address-of-the-identity")]
    public async Task should_answer_304_to_a_matching_etag_or_a_star_and_keep_the_policy(string query)
    {
        using var client = await _deployed.SignedInClientAsync();
        var path = query switch
        {
            "" => "/api/v1/identity/logo",
            "the-address-of-the-identity" => (await client.GetFromJsonAsync<IdentityResource>("/api/v1/identity", TestContext.Current.CancellationToken))!.LogoUrl!,
            _ => $"/api/v1/identity/logo?{query}",
        };
        var first = await client.GetAsync(path, TestContext.Current.CancellationToken);

        foreach (var validator in new[] { first.Headers.ETag!.ToString(), "*" })
        {
            using var again = new HttpRequestMessage(HttpMethod.Get, path);
            again.Headers.TryAddWithoutValidation("If-None-Match", validator);

            var response = await client.SendAsync(again, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.NotModified, validator);
            response.Headers.GetValues("Content-Security-Policy").Should().Equal("default-src 'none'; sandbox");
        }
    }
}
