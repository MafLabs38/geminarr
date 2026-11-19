using Geminarr.Core.Access;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Configuration;

/// <summary>A setting that cannot work stops the start with a sentence that names it, instead of failing later in a way that does not.</summary>
public sealed class GeminarrOptionsValidator : IValidateOptions<GeminarrOptions>
{
    /// <summary>The longest address Mnemarr takes as the link of an attention (its own limit).</summary>
    public const int MaxPublicUrlLength = 512;

    public ValidateOptionsResult Validate(string? name, GeminarrOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DataFolder))
        {
            return ValidateOptionsResult.Fail("DataFolder (GEMINARR__DATAFOLDER) must name the folder that holds the data.");
        }

        if (AllowedHostList.Split(options.AllowedHostNames).Count == 0)
        {
            return ValidateOptionsResult.Fail("AllowedHostNames (GEMINARR__ALLOWEDHOSTNAMES) must list at least one name (or * for every name).");
        }

        if (options.AllowedHostNames.Contains('/'))
        {
            return ValidateOptionsResult.Fail("AllowedHostNames (GEMINARR__ALLOWEDHOSTNAMES) lists host names, not addresses: write geminarr.homelab.example, not https://geminarr.homelab.example.");
        }

        if (options.PublicUrl is { Length: > MaxPublicUrlLength })
        {
            return ValidateOptionsResult.Fail($"PublicUrl (GEMINARR__PUBLICURL) is at most {MaxPublicUrlLength} characters.");
        }

        return string.IsNullOrWhiteSpace(options.PublicUrl) || AllowedHostList.HostOfPublicUrl(options.PublicUrl) is not null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("PublicUrl (GEMINARR__PUBLICURL) must be an http or https address with a host, and no user name, for example https://geminarr.homelab.example.");
    }
}
