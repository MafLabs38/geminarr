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
}
