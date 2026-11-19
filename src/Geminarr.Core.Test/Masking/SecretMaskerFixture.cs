using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Geminarr.Core.Masking;

namespace Geminarr.Core.Test.Masking;

/// <summary>The masking gives the answers of the file of samples that Mnemarr's masking and its collector are run against too: the engines must not drift.</summary>
public sealed partial class SecretMaskerFixture
{
    [GeneratedRegex(@"<([AUD]):(\d+)>")]
    private static partial Regex Marker();

    /// <summary>A marker stands for a run of characters, so that the file holds nothing a secret scanner would take for a secret.</summary>
    private static string Expand(string text) =>
        Marker().Replace(text, match => new string(match.Groups[1].Value switch { "A" => 'a', "U" => 'A', _ => '-' }, int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));

    public static TheoryData<string> Names()
    {
        var names = new TheoryData<string>();
        foreach (var sample in Samples().EnumerateArray())
        {
            names.Add(sample.GetProperty("name").GetString()!);
        }

        return names;
    }

    private static JsonElement Samples()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "masking", "samples.json")));
        return document.RootElement.GetProperty("samples").Clone();
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void should_give_the_answer_of_the_shared_samples(string name)
    {
        var sample = Samples().EnumerateArray().Single(candidate => candidate.GetProperty("name").GetString() == name);

        SecretMasker.Mask(Expand(sample.GetProperty("input").GetString()!)).Should().Be(Expand(sample.GetProperty("expected").GetString()!));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void should_give_the_same_text_when_it_masks_what_it_has_already_masked_for_every_shared_sample(string name)
    {
        var sample = Samples().EnumerateArray().Single(candidate => candidate.GetProperty("name").GetString() == name);
        var once = SecretMasker.Mask(Expand(sample.GetProperty("input").GetString()!));

        SecretMasker.Mask(once).Should().Be(once);
    }

    [Fact]
    public void should_give_the_same_text_when_it_masks_a_text_that_it_has_masked()
    {
        // A turn is masked when the extension hands it over and again when the pass queues it: the anchor of a conversation is found again only if the second mask changes nothing.
        var once = SecretMasker.Mask("my token is ghp_" + new string('a', 36) + " and that is all");

        SecretMasker.Mask(once).Should().Be(once);
    }

    [Fact]
    public void should_finish_in_time_on_the_longest_text_made_of_the_characters_of_a_scheme()
    {
        var text = new string('a', Geminarr.Core.Extension.ExtensionService.MaxTurnLength);

        // A rule that backtracks on this text would throw (every rule has a time limit), not hang.
        SecretMasker.Mask(text).Should().Be(text);
    }

    [Fact]
    public void should_hide_a_private_key_on_several_lines()
    {
        // Built here, so that no file holds the shape of a key (a secret scanner would take it for one).
        var kind = "RSA " + "PRIVATE KEY";
        var text = $"my key:\n-----BEGIN {kind}-----\n" + new string('a', 40) + $"\n-----END {kind}-----\nthanks";

        SecretMasker.Mask(text).Should().Be("my key:\n[masked:private-key]\nthanks");
    }

    [Fact]
    public void should_hide_a_private_key_whose_last_line_the_page_does_not_show()
    {
        var kind = "RSA " + "PRIVATE KEY";
        var body = string.Join('\n', Enumerable.Repeat(new string('a', 64), 3));
        var text = $"my key:\n-----BEGIN {kind}-----\n{body}\n\nthanks";

        var masked = SecretMasker.Mask(text);

        masked.Should().Be("my key:\n[masked:private-key]\n\nthanks");
        masked.Should().NotContain("aaaaaaaa");
    }

    [Fact]
    public void should_hide_the_first_line_of_a_private_key_that_is_the_last_thing_shown()
    {
        var kind = "OPENSSH " + "PRIVATE KEY";

        SecretMasker.Mask($"here: -----BEGIN {kind}-----").Should().Be("here: [masked:private-key]");
    }
}
