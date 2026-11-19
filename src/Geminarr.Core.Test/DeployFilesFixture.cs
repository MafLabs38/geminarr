namespace Geminarr.Core.Test;

/// <summary>The files of <c>deploy/</c> that say the same thing in two places must say it the same way.</summary>
public sealed class DeployFilesFixture
{
    private static string? RepositoryFile(string relative)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    [Fact]
    public void should_hold_in_the_config_map_of_the_kubernetes_example_the_very_script_that_the_compose_example_mounts()
    {
        var scriptFile = RepositoryFile("deploy/browser/loopback-only.sh");
        var manifestFile = RepositoryFile("deploy/kubernetes/geminarr.example.yaml");
        scriptFile.Should().NotBeNull("the files of deploy/ are in the repository that the tests run from");
        manifestFile.Should().NotBeNull();

        var script = File.ReadAllText(scriptFile!).TrimEnd('\n');
        var lines = File.ReadAllLines(manifestFile!);
        var start = Array.FindIndex(lines, line => line.StartsWith("  10-loopback-only.sh: |", StringComparison.Ordinal));
        start.Should().BeGreaterThan(0, "the manifest has a config map with the script");
        var block = lines.Skip(start + 1).TakeWhile(line => line.Length == 0 || line.StartsWith("    ", StringComparison.Ordinal)).Select(line => line.Length == 0 ? string.Empty : line[4..]);

        string.Join('\n', block).TrimEnd('\n').Should().Be(script, "a script that is only half in the config map would leave the desktop open on Kubernetes (keep them identical)");
    }

    private static string[] Rules(IEnumerable<string> lines) =>
        [.. lines.Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))];

    [Theory]
    [InlineData("rules.v4", "deploy/egress/rules.v4.example")]
    [InlineData("rules.v6", "deploy/egress/rules.v6.example")]
    public void should_hold_in_the_config_map_of_the_kubernetes_example_the_rules_of_the_example_file_that_the_compose_example_mounts(string key, string example)
    {
        var manifestFile = RepositoryFile("deploy/kubernetes/geminarr.example.yaml");
        var exampleFile = RepositoryFile(example);
        manifestFile.Should().NotBeNull();
        exampleFile.Should().NotBeNull();

        var lines = File.ReadAllLines(manifestFile!);
        var start = Array.FindIndex(lines, line => line.StartsWith($"  {key}: |", StringComparison.Ordinal));
        start.Should().BeGreaterThan(0, "the manifest has a config map with the rules");
        var block = lines.Skip(start + 1).TakeWhile(line => line.Length == 0 || line.StartsWith("    ", StringComparison.Ordinal)).Select(line => line.Length == 0 ? string.Empty : line[4..]);

        Rules(block).Should().Equal(Rules(File.ReadAllLines(exampleFile!)), "rules that differ between the two examples would leave Geminarr's process wider on one of them");
    }

    [Theory]
    [InlineData("deploy/egress/rules.v4.example")]
    [InlineData("deploy/egress/rules.v6.example")]
    [InlineData("tests/e2e/stack/egress/rules.v4")]
    [InlineData("tests/e2e/stack/egress/rules.v6")]
    public void should_name_geminarrs_process_by_the_user_of_its_image_and_refuse_everything_else_in_each_rule_file(string file)
    {
        var dockerfile = File.ReadAllText(RepositoryFile("Dockerfile")!);
        var user = System.Text.RegularExpressions.Regex.Match(dockerfile, @"^USER (\d+):", System.Text.RegularExpressions.RegexOptions.Multiline, TimeSpan.FromSeconds(1)).Groups[1].Value;
        var rules = Rules(File.ReadAllLines(RepositoryFile(file)!));

        user.Should().NotBeEmpty("the image says which user it runs as");
        rules.Where(rule => rule.StartsWith("-A OUTPUT", StringComparison.Ordinal)).Should().OnlyContain(rule => rule.Contains($"--uid-owner {user}", StringComparison.Ordinal), "only Geminarr's process is named: the browser keeps what the network allows");
        rules.Should().Contain(rule => rule.EndsWith("-j REJECT", StringComparison.Ordinal), "what is not allowed is refused");
        rules.Last().Should().Be("COMMIT");
    }

    [Fact]
    public void should_pin_the_same_image_of_the_egress_guard_in_the_examples_and_the_end_to_end_stack()
    {
        var pattern = new System.Text.RegularExpressions.Regex(@"registry\.k8s\.io/build-image/distroless-iptables@sha256:[0-9a-f]{64}", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        var images = new[] { "deploy/docker-compose.example.yml", "deploy/kubernetes/geminarr.example.yaml", "tests/e2e/stack/docker-compose.yml" }
            .SelectMany(file => pattern.Matches(File.ReadAllText(RepositoryFile(file)!)).Select(match => match.Value))
            .Distinct()
            .ToList();

        images.Should().ContainSingle("the guard that is tested is the one that the examples give (a digest, never a tag)");
    }

    [Theory]
    [InlineData("deploy/docker-compose.example.yml")]
    [InlineData("deploy/kubernetes/geminarr.example.yaml")]
    public void should_publish_neither_the_debugging_port_nor_the_desktop_of_the_browser_in_the_examples(string file)
    {
        var lines = File.ReadAllLines(RepositoryFile(file)!).Where(line => !line.TrimStart().StartsWith('#'));
        var published = lines.Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*(-\s*)?(['""]?[\d.:]*\d['""]?:)?(9222|3000)['""]?\s*$|^\s*(containerPort|port|targetPort):\s*(9222|3000)\s*$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1))).ToList();

        published.Should().BeEmpty("the browser's debugging port and desktop are for Geminarr alone: they are on the loopback of the shared namespace and in no port, no Service");
    }
}
