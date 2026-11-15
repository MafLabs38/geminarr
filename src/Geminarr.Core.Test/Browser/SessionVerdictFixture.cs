using Geminarr.Core.Browser;
using Geminarr.Core.Connection;
using Geminarr.Core.Pipeline;

namespace Geminarr.Core.Test.Browser;

public sealed class SessionVerdictFixture
{
    [Theory]
    [InlineData(PageKind.Assistant, true, SessionCheck.SignedIn)]
    [InlineData(PageKind.Assistant, false, SessionCheck.SignedOut)]
    [InlineData(PageKind.SignIn, true, SessionCheck.SignedOut)]
    [InlineData(PageKind.SignIn, false, SessionCheck.SignedOut)]
    public void should_say_signed_out_when_sent_to_sign_in_or_without_the_cookie_of_an_account_and_signed_in_on_the_assistants_page_with_it(PageKind page, bool cookie, SessionCheck expected)
    {
        SessionVerdict.Decide(page, cookie).Should().Be(expected);
    }

    [Theory]
    [InlineData("SID;__Secure-1PSID", new[] { "NID", "SID" }, true)]
    [InlineData("SID;__Secure-1PSID", new[] { "NID", "AEC" }, false)]
    [InlineData("SID;__Secure-1PSID", new string[0], false)]
    [InlineData("SID", new[] { "sid" }, false)]
    [InlineData("", new[] { "NID" }, true)]
    public void should_look_for_the_names_of_the_cookies_of_a_signed_in_account_and_ask_nothing_when_the_list_is_empty(string wanted, string[] held, bool present)
    {
        SessionVerdict.HasSessionCookie(held, new BrowserOptions { SessionCookies = wanted }).Should().Be(present);
    }

    [Fact]
    public void should_say_the_reader_is_broken_when_the_page_is_neither_the_assistants_nor_a_sign_in()
    {
        var decide = () => SessionVerdict.Decide(PageKind.Other, sessionCookiePresent: true);

        decide.Should().Throw<ReaderException>().Which.Reason.Should().Be(ConnectionReasons.ReaderBroken);
    }

}
