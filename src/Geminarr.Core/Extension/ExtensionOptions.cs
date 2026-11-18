using Microsoft.Extensions.Options;

namespace Geminarr.Core.Extension;

/// <summary>
/// The extension that reads the assistant in the browser container (ADR 0015), from the <c>Extension</c> section: the token that it presents, where its package is, and how long a silence is too long.
/// </summary>
public sealed class ExtensionOptions
{
    public const string SectionName = "Extension";

    /// <summary>The file that holds the token that the extension presents (a secret mounted as a file, never an environment variable). Without it nothing that the extension sends is accepted.</summary>
    public string? TokenFile { get; set; }

    /// <summary>The folder of the extension's package (<c>extension.json</c>, the signed <c>.crx</c>, the selectors), relative to the application unless it is absolute.</summary>
    public string PackageFolder { get; set; } = "extension";

    /// <summary>Minutes without a word from the extension after which it is taken for silent: the browser is not running it, or cannot reach Geminarr.</summary>
    public int SilentAfterMinutes { get; set; } = 45;
}

public sealed class ExtensionOptionsValidator : IValidateOptions<ExtensionOptions>
{
    public ValidateOptionsResult Validate(string? name, ExtensionOptions options)
    {
        if (options.SilentAfterMinutes is < 1 or > 1_440)
        {
            return ValidateOptionsResult.Fail("Extension:SilentAfterMinutes (GEMINARR__EXTENSION__SILENTAFTERMINUTES) must be between 1 and 1440.");
        }

        return string.IsNullOrWhiteSpace(options.PackageFolder)
            ? ValidateOptionsResult.Fail("Extension:PackageFolder (GEMINARR__EXTENSION__PACKAGEFOLDER) cannot be empty.")
            : ValidateOptionsResult.Success;
    }
}
