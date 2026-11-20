using Geminarr.Core.Accounts;

namespace Geminarr.Core.Test.Accounts;

public sealed class AccountKeyFixture
{
    [Fact]
    public void should_make_the_same_key_for_the_same_address_whatever_its_case_and_its_spaces()
    {
        AccountKey.Of("Jane.Doe@Example.org").Should().Be(AccountKey.Of("  jane.doe@example.org "));
    }

    [Fact]
    public void should_make_another_key_for_another_address()
    {
        AccountKey.Of("jane.doe@example.org").Should().NotBe(AccountKey.Of("jane.doe@example.com"));
    }

    [Fact]
    public void should_make_a_key_of_sixteen_hexadecimal_characters_that_says_nothing_of_the_address()
    {
        var key = AccountKey.Of("jane.doe@example.org");

        key.Should().MatchRegex("^[0-9a-f]{16}$");
        key.Should().NotContain("jane");
        AccountKey.IsValid(key).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcde")]
    [InlineData("0123456789abcdeff")]
    [InlineData("0123456789ABCDEF")]
    [InlineData("../../etc/passwd")]
    [InlineData("0123456789abcde/")]
    public void should_refuse_as_a_key_what_could_not_be_the_name_of_a_folder_of_ours(string? text)
    {
        AccountKey.IsValid(text).Should().BeFalse();
    }
}
