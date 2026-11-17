using Geminarr.Core.Pipeline;
using Geminarr.Host.Pipeline;
using Geminarr.Integration.Test.Support;

namespace Geminarr.Integration.Test;

public sealed class SignInWatcherFixture
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);

    [Fact]
    public async Task should_ask_for_a_pass_when_the_sign_in_page_that_was_open_is_gone()
    {
        var browser = new FakeBrowserHost { SigningIn = true };
        using var trigger = new PassTrigger();
        using var subject = new SignInWatcher(browser, trigger, Tick);
        await subject.StartAsync(Token);
        await Task.Delay(100, Token);
        (await trigger.WaitAsync(TimeSpan.Zero, Token)).Should().BeFalse("somebody is typing: nothing is asked while the page is open");

        browser.SigningIn = false;

        (await trigger.WaitAsync(TimeSpan.FromSeconds(5), Token)).Should().BeTrue("the person has finished: the session is looked at now");
        await subject.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task should_ask_for_nothing_while_nobody_signs_in()
    {
        var browser = new FakeBrowserHost { SigningIn = false };
        using var trigger = new PassTrigger();
        using var subject = new SignInWatcher(browser, trigger, Tick);

        await subject.StartAsync(Token);
        await Task.Delay(150, Token);
        await subject.StopAsync(CancellationToken.None);

        (await trigger.WaitAsync(TimeSpan.Zero, Token)).Should().BeFalse();
    }
}
