using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;
using Geminarr.Core.Test.Support;

namespace Geminarr.Core.Test.Pipeline;

public sealed class WebReaderFixture
{
    [Fact]
    public async Task should_say_there_is_no_profile_without_starting_a_browser_when_nobody_signed_in()
    {
        var browser = new FakeBrowser();

        var check = await new WebReader(browser).CheckSessionAsync(TestContext.Current.CancellationToken);

        check.Should().Be(SessionCheck.NoProfile);
        browser.Checks.Should().Be(0);
    }

    [Theory]
    [InlineData(SessionCheck.SignedIn)]
    [InlineData(SessionCheck.SignedOut)]
    public async Task should_ask_the_browser_when_a_person_has_signed_in(SessionCheck expected)
    {
        var browser = new FakeBrowser { State = new ProfileState(Exists: true, HasSession: true, Private: true), Check = expected };

        var check = await new WebReader(browser).CheckSessionAsync(TestContext.Current.CancellationToken);

        check.Should().Be(expected);
        browser.Checks.Should().Be(1);
    }

    [Fact]
    public async Task should_list_nothing_yet_and_say_so_when_asked_to_open_a_conversation()
    {
        var reader = new WebReader(new FakeBrowser());

        (await reader.ListRecentAsync(40, TestContext.Current.CancellationToken)).Should().BeEmpty();
        reader.Source.Should().Be("web");
        var open = () => reader.OpenAsync("c1", TestContext.Current.CancellationToken);
        (await open.Should().ThrowAsync<ReaderException>()).Which.Reason.Should().Be(ConnectionReasons.ReaderBroken);
    }
}
