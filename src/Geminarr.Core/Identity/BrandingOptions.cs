using Microsoft.Extensions.Options;

namespace Geminarr.Core.Identity;

/// <summary>The colors of the two texts of the badge: a hex color or a theme token (see <see cref="IdentityRules"/>); the accent when empty.</summary>
public sealed class BrandingColorOptions
{
    public string? Name { get; set; }

    public string? Version { get; set; }
}

/// <summary>
/// What a deployment says about how this Geminarr presents itself, from the <c>Identity</c> section (ADR 0013): the name of the environment, the colors and an image
/// file mounted into the container. It is what keeps a personal picture out of this public repository: each environment says its own in the repository that describes it.
/// </summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Identity";

    /// <summary>The name shown at the bottom of the menu; <see cref="IdentityRules.DefaultName"/> when empty.</summary>
    public string? Name { get; set; }

    public BrandingColorOptions Colors { get; set; } = new();

    /// <summary>The path of an image (png, webp or svg, checked by its content) that is the logo: a file mounted into the container. The product's own drawing when empty.</summary>
    public string? LogoFile { get; set; }
}

public sealed class BrandingOptionsValidator : IValidateOptions<BrandingOptions>
{
    public ValidateOptionsResult Validate(string? name, BrandingOptions options)
    {
        var problem = (options.Name?.Trim() is { Length: > 0 } text ? IdentityRules.ValidateName(text) : null)
            ?? (options.Colors.Name is { Length: > 0 } nameColor ? IdentityRules.ValidateColor("name", nameColor) : null)
            ?? (options.Colors.Version is { Length: > 0 } versionColor ? IdentityRules.ValidateColor("version", versionColor) : null);
        return problem is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail($"Identity (GEMINARR__IDENTITY__*): {problem}");
    }
}
