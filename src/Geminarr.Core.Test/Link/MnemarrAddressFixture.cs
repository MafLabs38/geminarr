using Geminarr.Core.Link;
using Geminarr.Core.Test.Access;

namespace Geminarr.Core.Test.Link;

public sealed class MnemarrAddressFixture
{
    [Theory]
    [InlineData("https://mnemarr.homelab.example", "https://mnemarr.homelab.example")]
    [InlineData("https://mnemarr.homelab.example/", "https://mnemarr.homelab.example")]
    [InlineData("  http://192.168.1.10:8383/  ", "http://192.168.1.10:8383")]
    [InlineData("http://localhost:8383", "http://localhost:8383")]
    public void should_keep_the_address_without_its_slash(string address, string expected)
    {
        MnemarrAddress.Normalize(address).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mnemarr.homelab.example")]
    [InlineData("ftp://mnemarr.homelab.example")]
    [InlineData("https://user:secret@mnemarr.homelab.example")]
    [InlineData("https://mnemarr.homelab.example/path")]
    [InlineData("https://mnemarr.homelab.example/?x=1")]
    [InlineData("https://mnemarr.homelab.example/#top")]
    public void should_refuse_what_is_not_the_address_of_a_mnemarr(string? address)
    {
        MnemarrAddress.Normalize(address).Should().BeNull();
    }

    [Theory]
    [InlineData("https://mnemarr.homelab.example", true)]
    [InlineData("http://localhost:8383", true)]
    [InlineData("http://127.0.0.1:8383", true)]
    [InlineData("http://[::1]:8383", true)]
    [InlineData("http://mnemarr.homelab.example", false)]
    [InlineData("http://192.168.1.10:8383", false)]
    public void should_trust_https_and_the_loopback_only(string address, bool secure)
    {
        MnemarrAddress.IsSecure(address).Should().Be(secure);
    }

    [Fact]
    public void should_validate_the_options_and_name_the_setting()
    {
        var validator = new MnemarrOptionsValidator();

        validator.Validate(null, new MnemarrOptions()).Succeeded.Should().BeTrue();
        validator.Validate(null, new MnemarrOptions { Url = "https://mnemarr.homelab.example", Machine = "gemini-2" }).Succeeded.Should().BeTrue();
        validator.Validate(null, new MnemarrOptions { Url = "not an address" }).FailureMessage.Should().Contain("GEMINARR__MNEMARR__URL");
        foreach (var machine in new[] { "", "Gemini", "-gemini", "gemini-", "a b", new string('a', 41) })
        {
            validator.Validate(null, new MnemarrOptions { Machine = machine }).FailureMessage.Should().Contain("GEMINARR__MNEMARR__MACHINE", machine);
        }
    }

    [Fact]
    public void should_keep_a_key_in_a_file_only_its_owner_reads_and_forget_it_on_request()
    {
        var folder = Directory.CreateTempSubdirectory("geminarr-key-").FullName;
        try
        {
            var store = new KeyStore(Options.Of(new MnemarrOptions()), Options.Of(new Geminarr.Core.Configuration.GeminarrOptions { DataFolder = folder }));
            store.Read().Should().BeNull();

            store.Write("mnm_secret-key");
            store.Write("mnm_other-key");

            store.Read().Should().Be("mnm_other-key");
            var file = Path.Combine(folder, "mnemarr", "key");
            File.Exists(file).Should().BeTrue();
            File.Exists(file + ".new").Should().BeFalse();
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(file).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.GetUnixFileMode(Path.Combine(folder, "mnemarr")).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            store.Delete();
            store.Read().Should().BeNull();
            store.Delete();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void should_read_the_key_from_the_file_the_deployment_names_and_treat_an_empty_or_bad_one_as_none()
    {
        var folder = Directory.CreateTempSubdirectory("geminarr-key-").FullName;
        try
        {
            var file = Path.Combine(folder, "given-key");
            File.WriteAllText(file, "mnm_given\n");
            new KeyStore(Options.Of(new MnemarrOptions { KeyFile = file }), Options.Of(new Geminarr.Core.Configuration.GeminarrOptions())).Read().Should().Be("mnm_given");

            File.WriteAllText(file, "\n");
            new KeyStore(Options.Of(new MnemarrOptions { KeyFile = file }), Options.Of(new Geminarr.Core.Configuration.GeminarrOptions())).Read().Should().BeNull();
            new KeyStore(Options.Of(new MnemarrOptions { KeyFile = "bad\0path" }), Options.Of(new Geminarr.Core.Configuration.GeminarrOptions())).Read().Should().BeNull();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
