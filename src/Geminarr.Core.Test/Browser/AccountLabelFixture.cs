using Geminarr.Core.Browser;

namespace Geminarr.Core.Test.Browser;

public sealed class AccountLabelFixture
{
    [Fact]
    public void should_read_the_name_and_the_address_of_the_label_of_the_account_button()
    {
        var identity = AccountLabel.Parse(["Google Account: Jane Doe (jane.doe@example.org)"]);

        identity.Should().Be(new AccountIdentity("Jane Doe", "jane.doe@example.org"));
    }

    [Fact]
    public void should_take_the_name_from_a_label_that_has_one_when_the_first_does_not()
    {
        // The two links of the page do not say it the same way: the first has no colon (its words are the page's language, not guessed), the second has.
        var identity = AccountLabel.Parse(["Compte Google Jane Doe\n(jane@example.org), more", "Compte Google : Jane Doe (jane@example.org)"]);

        identity.Should().Be(new AccountIdentity("Jane Doe", "jane@example.org"));
    }

    [Fact]
    public void should_know_the_address_alone_when_no_label_gives_a_name()
    {
        AccountLabel.Parse(["Google Account (jane@example.org)"]).Should().Be(new AccountIdentity(null, "jane@example.org"));
    }

    [Fact]
    public void should_take_the_address_and_keep_parentheses_that_belong_to_the_name()
    {
        AccountLabel.Parse(["Google Account: Jane (Doe) (jane@example.org)"]).Should().Be(new AccountIdentity("Jane (Doe)", "jane@example.org"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sign in")]
    [InlineData("Google Account: nobody (not-an-address)")]
    public void should_know_nobody_when_no_label_holds_an_address(string? label)
    {
        AccountLabel.Parse([label, " "]).Should().BeNull();
    }
}
