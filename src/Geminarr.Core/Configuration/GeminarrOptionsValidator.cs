using Microsoft.Extensions.Options;

namespace Geminarr.Core.Configuration;

/// <summary>A setting that cannot work stops the start with a sentence that names it, instead of failing later in a way that does not.</summary>
public sealed class GeminarrOptionsValidator : IValidateOptions<GeminarrOptions>
{
    public ValidateOptionsResult Validate(string? name, GeminarrOptions options) =>
        string.IsNullOrWhiteSpace(options.DataFolder)
            ? ValidateOptionsResult.Fail("DataFolder (GEMINARR__DATAFOLDER) must name the folder that holds the data.")
            : ValidateOptionsResult.Success;
}
