using Geminarr.Core.Accounts;
using Geminarr.Core.Link;

namespace Geminarr.Core.Test.Accounts;

public sealed class MachineNameFixture
{
    private static string Name(string address, Func<string, bool>? taken = null) => MachineName.Of(address, AccountKey.Of(address), taken ?? (_ => false));

    [Theory]
    [InlineData("jane.doe@example.org", "geminarr-jane-doe")]
    [InlineData("Jane_Doe+news@example.org", "geminarr-jane-doe-news")]
    [InlineData("jane@example.org", "geminarr-jane")]
    [InlineData("jane00@example.org", "geminarr-jane00")]
    [InlineData("--jane..doe--@example.org", "geminarr-jane-doe")]
    public void should_make_the_name_from_the_part_before_the_at_sign(string address, string expected)
    {
        Name(address).Should().Be(expected);
    }

    [Theory]
    [InlineData("élodie@example.org")]
    [InlineData("@example.org")]
    [InlineData("....@example.org")]
    public void should_make_a_name_that_mnemarr_takes_whatever_the_address_is(string address)
    {
        MnemarrOptionsValidator.IsValidMachine(Name(address)).Should().BeTrue(Name(address));
    }

    [Fact]
    public void should_name_an_address_that_has_nothing_to_keep_after_the_prefix()
    {
        Name("....@example.org").Should().Be("geminarr-account");
    }

    [Fact]
    public void should_cut_a_long_name_at_forty_characters_and_not_end_it_with_a_hyphen()
    {
        var name = Name(new string('a', 30) + "-" + new string('b', 30) + "@example.org");

        name.Length.Should().BeLessThanOrEqualTo(40);
        MnemarrOptionsValidator.IsValidMachine(name).Should().BeTrue(name);
    }

    [Fact]
    public void should_add_four_characters_of_the_key_when_another_account_has_the_name()
    {
        var key = AccountKey.Of("jane.doe@example.com");

        var name = MachineName.Of("jane.doe@example.com", key, taken => taken == "geminarr-jane-doe");

        name.Should().Be("geminarr-jane-doe-" + key[..4]);
    }

    [Fact]
    public void should_keep_the_suffix_within_forty_characters_when_the_name_is_long_and_taken()
    {
        var address = new string('a', 60) + "@example.org";
        var key = AccountKey.Of(address);

        var name = MachineName.Of(address, key, taken => taken.Length == 40 && !taken.EndsWith(key[..4], StringComparison.Ordinal));

        name.Length.Should().BeLessThanOrEqualTo(40);
        name.Should().EndWith("-" + key[..4]);
        MnemarrOptionsValidator.IsValidMachine(name).Should().BeTrue(name);
    }

    [Fact]
    public void should_give_the_same_name_for_the_same_address()
    {
        Name("jane.doe@example.org").Should().Be(Name("JANE.DOE@example.org"));
    }
}
