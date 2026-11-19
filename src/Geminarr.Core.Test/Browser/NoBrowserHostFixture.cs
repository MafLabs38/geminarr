using Geminarr.Core.Browser;

namespace Geminarr.Core.Test.Browser;

/// <summary>The browser of a host that has none: nothing configured, nothing to look at.</summary>
public sealed class NoBrowserHostFixture
{
    [Fact]
    public async Task should_have_nothing_to_look_at()
    {
        var subject = new NoBrowserHost();

        subject.State().Should().Be(new BrowserState(Configured: false, Reachable: null));
        (await subject.ProbeAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
    }
}
