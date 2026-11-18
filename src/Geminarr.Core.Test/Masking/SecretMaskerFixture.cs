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

    [Fact]
    public void should_hide_a_private_key_on_several_lines()
    {
        // Built here, so that no file holds the shape of a key (a secret scanner would take it for one).
        var kind = "RSA " + "PRIVATE KEY";
        var text = $"my key:\n-----BEGIN {kind}-----\n" + new string('a', 40) + $"\n-----END {kind}-----\nthanks";

        SecretMasker.Mask(text).Should().Be("my key:\n[masked:private-key]\nthanks");
    }
}
