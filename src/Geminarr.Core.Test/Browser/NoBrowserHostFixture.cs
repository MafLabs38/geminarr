using Geminarr.Core.Browser;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Browser;

/// <summary>The browser of a host that has none: nothing configured, nothing to check, to open or to clear.</summary>
public sealed class NoBrowserHostFixture
{
    [Fact]
    public async Task should_have_nothing_to_check_to_open_or_to_clear()
    {
        var subject = new NoBrowserHost();
        var token = TestContext.Current.CancellationToken;

        subject.State().Should().Be(new BrowserState(Configured: false, Reachable: null));
        (await subject.CheckSessionAsync(token)).Should().Be(SessionCheck.NoBrowser);
        (await subject.IsSigningInAsync(token)).Should().Be(false);
        await subject.OpenSignInPageAsync(token);
        await subject.DisconnectAsync(token);
    }
}
