using Geminarr.Core.Access;
using Geminarr.Core.Configuration;

namespace Geminarr.Core.Test.Access;

public sealed class AllowedHostListFixture
{
    private static AllowedHostList Subject(string hosts = "localhost;127.0.0.1;[::1]", string? publicUrl = null) =>
        new(Options.Of(new GeminarrOptions { AllowedHostNames = hosts, PublicUrl = publicUrl }));

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("localhost:8282", true)]
    [InlineData("LOCALHOST:8282", true)]
    [InlineData("127.0.0.1:8282", true)]
    [InlineData("[::1]:8282", true)]
    [InlineData("[::1]", true)]
    [InlineData("evil.example", false)]
    [InlineData("evil.example:8282", false)]
    [InlineData("localhost.evil.example", false)]
    [InlineData("192.168.1.10:8282", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("[::1", false)]
    public void should_allow_only_the_names_of_the_list_whatever_the_port_and_the_case(string? header, bool expected)
    {
        Subject().IsAllowed(header).Should().Be(expected);
    }

    [Fact]
    public void should_allow_the_host_of_the_public_address_as_well()
    {
        var subject = Subject(publicUrl: "https://geminarr.homelab.example");

        subject.IsAllowed("geminarr.homelab.example").Should().BeTrue();
        subject.IsAllowed("geminarr.homelab.example:443").Should().BeTrue();
        subject.IsAllowed("other.homelab.example").Should().BeFalse();
    }

    [Theory]
    [InlineData("localhost,192.168.1.10")]
    [InlineData("localhost;192.168.1.10")]
    [InlineData("localhost 192.168.1.10")]
    [InlineData("localhost:8282, 192.168.1.10:8282")]
    public void should_read_a_list_separated_by_commas_semicolons_or_spaces_and_ignore_the_ports(string setting)
    {
        Subject(setting).IsAllowed("192.168.1.10:8282").Should().BeTrue();
    }

    [Fact]
    public void should_allow_every_name_when_the_list_says_so()
    {
        Subject("*").IsAllowed("anything.example").Should().BeTrue();
    }

    [Theory]
    [InlineData("https://geminarr.homelab.example", "geminarr.homelab.example")]
    [InlineData("http://192.168.1.10:8282/", "192.168.1.10")]
    [InlineData("ftp://geminarr.homelab.example", null)]
    [InlineData("https://user:secret@geminarr.homelab.example", null)]
    [InlineData("geminarr.homelab.example", null)]
    [InlineData("", null)]
    public void should_take_the_host_of_a_valid_public_address_and_nothing_of_the_others(string address, string? expected)
    {
        AllowedHostList.HostOfPublicUrl(address).Should().Be(expected);
    }
}
