using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Browser;

public sealed class SessionVerdictFixture
{
    [Theory]
    [InlineData(false, PageKind.Assistant, SessionCheck.SignedIn)]
    [InlineData(true, PageKind.Assistant, SessionCheck.SignedOut)]
    [InlineData(true, PageKind.Other, SessionCheck.SignedOut)]
    [InlineData(false, PageKind.SignIn, SessionCheck.SignedOut)]
    public void should_say_signed_out_when_the_browser_was_sent_to_sign_in_and_signed_in_on_the_assistants_page(bool redirectSeen, PageKind page, SessionCheck expected)
    {
        SessionVerdict.Decide(redirectSeen, page).Should().Be(expected);
    }

    [Fact]
    public void should_say_the_reader_is_broken_when_the_page_is_neither_the_assistants_nor_a_sign_in()
    {
        var decide = () => SessionVerdict.Decide(signInRedirectSeen: false, PageKind.Other);

        decide.Should().Throw<ReaderException>().Which.Reason.Should().Be(ConnectionReasons.ReaderBroken);
    }

    [Theory]
    [InlineData(true, ConnectionReasons.ReaderBroken)]
    [InlineData(false, ConnectionReasons.Unreachable)]
    public void should_blame_the_page_when_the_policy_refused_it_and_the_network_otherwise(bool refused, string reason)
    {
        SessionVerdict.ReasonOfLoadFailure(refused).Should().Be(reason);
    }
}
