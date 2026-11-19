using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Extension;

/// <summary>The package that the pipeline built: the identifier that its key gives it, its version, and the signed file.</summary>
public sealed record ExtensionPackageInfo(string Id, string Version, string FileName, string FilePath);

/// <summary>Where the signed extension is, for the browser to install and to update from (ADR 0022).</summary>
public interface IExtensionPackage
{
    /// <summary>The package, or null when the image carries none (a build without it) or its description is not what the pipeline writes.</summary>
    ExtensionPackageInfo? Current();
}

/// <summary>Reads <c>extension.json</c> in the package folder at each call: a package replaced under a running container is served without a restart. What it says is checked, since it is a file.</summary>
public sealed partial class ExtensionPackage : IExtensionPackage
{
    public const string DescriptionFile = "extension.json";
    private const int MaxDescriptionBytes = 4 * 1024;

    private readonly IOptions<ExtensionOptions> _options;

    public ExtensionPackage(IOptions<ExtensionOptions> options)
    {
        _options = options;
    }

    public ExtensionPackageInfo? Current()
    {
        var folder = Path.GetFullPath(_options.Value.PackageFolder, AppContext.BaseDirectory);
        var description = Path.Combine(folder, DescriptionFile);
        try
        {
            if (!File.Exists(description) || new FileInfo(description).Length > MaxDescriptionBytes)
            {
                return null;
            }

            using var stream = File.OpenRead(description);
            var parsed = JsonSerializer.Deserialize<Description>(stream, JsonSerializerOptions.Web);
            if (parsed is not { Id: { } id, Version: { } version, File: { } file } || !IdPattern().IsMatch(id) || !VersionPattern().IsMatch(version) || !FilePattern().IsMatch(file) || file.Contains("..", StringComparison.Ordinal))
            {
                return null;
            }

            var path = Path.Combine(folder, file);
            return File.Exists(path) ? new ExtensionPackageInfo(id, version, file, path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private sealed record Description(string? Id, string? Version, string? File);

    [GeneratedRegex("^[a-p]{32}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^\d{1,4}(\.\d{1,4}){0,3}$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,80}\.crx$")]
    private static partial Regex FilePattern();
}
