using Microsoft.Extensions.Options;

namespace Geminarr.Core.Extension;

/// <summary>
/// The extension that reads the assistant in the browser container (ADR 0022), from the <c>Extension</c> section: the token that it presents, where its package is, and how long a silence is too long.
/// </summary>
public sealed class ExtensionOptions
{
    public const string SectionName = "Extension";

    /// <summary>The file that holds the token that the extension presents (a secret mounted as a file, never an environment variable). Without it nothing that the extension sends is accepted.</summary>
    public string? TokenFile { get; set; }

    /// <summary>The folder of the extension's package (<c>extension.json</c> and the signed <c>.crx</c>), relative to the application unless it is absolute.</summary>
    public string PackageFolder { get; set; } = "extension";

    /// <summary>The address by which the browser reaches Geminarr, when it is not the one of the request (a proxy that ends TLS): the update manifest of the extension names the package by it. Empty: the address of the request.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>Minutes without a word from the extension after which it is taken for silent: the browser is not running it, or cannot reach Geminarr.</summary>
    public int SilentAfterMinutes { get; set; } = 45;
}

public sealed class ExtensionOptionsValidator : IValidateOptions<ExtensionOptions>
{
    public ValidateOptionsResult Validate(string? name, ExtensionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PublicUrl) && !IsBaseUrl(options.PublicUrl))
        {
            return ValidateOptionsResult.Fail("Extension:PublicUrl (GEMINARR__EXTENSION__PUBLICURL) must be an http or https address without a path, such as https://geminarr.homelab.example.");
        }

        if (options.SilentAfterMinutes is < 1 or > 1_440)
        {
            return ValidateOptionsResult.Fail("Extension:SilentAfterMinutes (GEMINARR__EXTENSION__SILENTAFTERMINUTES) must be between 1 and 1440.");
        }

        return string.IsNullOrWhiteSpace(options.PackageFolder)
            ? ValidateOptionsResult.Fail("Extension:PackageFolder (GEMINARR__EXTENSION__PACKAGEFOLDER) cannot be empty.")
            : ValidateOptionsResult.Success;
    }

    private static bool IsBaseUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo);
}
