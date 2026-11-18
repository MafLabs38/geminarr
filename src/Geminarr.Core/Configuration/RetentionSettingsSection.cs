using System.Globalization;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Configuration;

/// <summary>How long the history of the passes is kept.</summary>
public sealed class RetentionSettingsSection : ISettingsSection
{
    private readonly IOptions<RetentionOptions> _options;

    public RetentionSettingsSection(IOptions<RetentionOptions> options)
    {
        _options = options;
    }

    public IReadOnlyList<SettingValue> Read() =>
    [
        new("GEMINARR__RETENTION__RUNSDAYS", _options.Value.RunsDays.ToString(CultureInfo.InvariantCulture), false),
    ];
}
