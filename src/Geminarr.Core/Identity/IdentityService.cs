using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Identity;

/// <summary>An image that is the logo: how it is served, the hash that goes in its address, when the file was written, and a way to read its bytes.</summary>
public sealed record IdentityLogoFile(string ContentType, string Hash, DateTimeOffset Modified, Func<byte[]?> Load);

/// <summary>The colors of the two texts, resolved: each is a theme token name or a hex color.</summary>
public sealed record IdentityColors(string Name, string Version);

/// <summary>How this Geminarr presents itself, resolved (ADR 0013). <c>LogoProblem</c> says why a logo that was given is not used.</summary>
public sealed record ResolvedIdentity(string Name, IdentityColors Colors, IdentityLogoFile? Logo, string? LogoProblem);

/// <summary>
/// The identity of this Geminarr, shown at the bottom of the menu: a name, an image and the version (ADR 0013). What the deployment said (<c>Identity</c> settings) wins over
/// the product's own (its name, its drawing, the accent). Everything that is given is checked before it is used, and the file is read again only when it changed.
/// </summary>
public sealed class IdentityService
{
    private readonly BrandingOptions _options;
    private readonly Lock _fileLock = new();
    private (string Path, DateTime Modified, long Length, IdentityLogoFile? Logo, string? Problem)? _file;

    public IdentityService(IOptions<BrandingOptions> options)
    {
        _options = options.Value;
    }

    public ResolvedIdentity Get()
    {
        var colors = new IdentityColors(Pick(_options.Colors.Name, IdentityRules.DefaultColor), Pick(_options.Colors.Version, IdentityRules.DefaultColor));
        var (logo, problem) = DeploymentLogo();
        return new ResolvedIdentity(Pick(_options.Name, IdentityRules.DefaultName), colors, logo, problem);
    }

    private static string Pick(string? deployed, string fallback) => string.IsNullOrWhiteSpace(deployed) ? fallback : deployed.Trim();

    /// <summary>The image that the deployment points to, read again only when the file changed.</summary>
    private (IdentityLogoFile? Logo, string? Problem) DeploymentLogo()
    {
        if (string.IsNullOrWhiteSpace(_options.LogoFile))
        {
            return (null, null);
        }

        lock (_fileLock)
        {
            var info = TryInfo(_options.LogoFile);
            if (info is null)
            {
                return (null, "The logo file of the settings cannot be read.");
            }

            if (_file is { } known && known.Path == info.FullName && info.Exists && known.Modified == info.LastWriteTimeUtc && known.Length == info.Length)
            {
                return (known.Logo, known.Problem);
            }

            var (logo, problem) = ReadDeploymentLogo(info);
            _file = (info.FullName, info.Exists ? info.LastWriteTimeUtc : default, info.Exists ? info.Length : 0, logo, problem);
            return (logo, problem);
        }
    }

    /// <summary>The file of a path, or null when the path is not one.</summary>
    private static FileInfo? TryInfo(string path)
    {
        try
        {
            return new FileInfo(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static (IdentityLogoFile? Logo, string? Problem) ReadDeploymentLogo(FileInfo info)
    {
        try
        {
            if (!info.Exists)
            {
                return (null, "The logo file of the settings is not there.");
            }

            if (info.Length > IdentityRules.MaxLogoBytes)
            {
                return (null, $"The logo file of the settings is larger than {IdentityRules.MaxLogoBytes / 1024} KiB.");
            }

            var content = File.ReadAllBytes(info.FullName);
            var (format, error) = IdentityRules.InspectLogo(content, info.Extension);
            return format is null
                ? (null, $"The logo file of the settings is not used: {error}")
                : (new IdentityLogoFile(format.ContentType, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), info.LastWriteTimeUtc, () => content), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "The logo file of the settings cannot be read.");
        }
    }
}
