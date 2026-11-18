using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Geminarr.Integration.Test;

/// <summary>The host that looks at the browser's desktop, and nothing else (ADR 0021): it asks one question and follows nothing.</summary>
public sealed class DesktopBrowserHostFixture : IAsyncDisposable
{
    private WebApplication? _desktop;
    private int _followed;

    private async Task<DesktopBrowserHost> SubjectAsync(string path, bool redirect)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _desktop = builder.Build();
        _desktop.MapGet("/", () => redirect ? Results.Redirect("/elsewhere") : Results.Ok("desktop"));
        _desktop.MapGet("/elsewhere", () =>
        {
            Interlocked.Increment(ref _followed);
            return Results.Ok("elsewhere");
        });
        await _desktop.StartAsync(TestContext.Current.CancellationToken);
        var address = _desktop.Urls.First() + path;

        var services = new ServiceCollection();
        services.AddHttpClient(nameof(DesktopBrowserHost)).ConfigurePrimaryHttpMessageHandler(DesktopBrowserHost.NewHandler);
        var provider = services.BuildServiceProvider();
        return new DesktopBrowserHost(Options.Create(new BrowserOptions { DesktopUrl = address }), provider.GetRequiredService<IHttpClientFactory>());
    }

    [Fact]
    public async Task should_say_that_it_checks_nothing_of_the_session_while_the_desktop_answers()
    {
        var subject = await SubjectAsync("/", redirect: false);

        (await subject.CheckSessionAsync(TestContext.Current.CancellationToken)).Should().Be(SessionCheck.NoBrowser);

        subject.State().Should().Be(new BrowserState(Configured: true, Reachable: true));
    }

    [Fact]
    public async Task should_not_follow_a_redirect_of_the_desktop_to_another_address()
    {
        var subject = await SubjectAsync("/", redirect: true);

        (await subject.CheckSessionAsync(TestContext.Current.CancellationToken)).Should().Be(SessionCheck.NoBrowser);

        _followed.Should().Be(0);
    }

    [Fact]
    public async Task should_say_that_the_browser_container_cannot_be_reached_when_the_desktop_does_not_answer()
    {
        var subject = await SubjectAsync("/", redirect: false);
        await _desktop!.StopAsync(TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<ReaderException>(() => subject.CheckSessionAsync(TestContext.Current.CancellationToken));

        failure.Reason.Should().Be(ConnectionReasons.BrowserFailed);
        subject.State().Reachable.Should().BeFalse();
    }

    [Fact]
    public async Task should_refuse_to_disconnect_with_a_sentence_of_ours()
    {
        var subject = await SubjectAsync("/", redirect: false);

        await Assert.ThrowsAsync<ConnectionRefusedException>(() => subject.DisconnectAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task should_have_nothing_to_check_without_a_desktop()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(nameof(DesktopBrowserHost));
        var subject = new DesktopBrowserHost(Options.Create(new BrowserOptions()), services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());

        (await subject.CheckSessionAsync(TestContext.Current.CancellationToken)).Should().Be(SessionCheck.NoBrowser);

        subject.State().Configured.Should().BeFalse();
    }

    public async ValueTask DisposeAsync()
    {
        if (_desktop is not null)
        {
            await _desktop.DisposeAsync();
        }
    }
}
