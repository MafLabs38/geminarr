using System.Globalization;
using Geminarr.Core.Extension;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Configuration;

/// <summary>The settings of the extension that reads the assistant. The token is a secret: only whether it is set is shown.</summary>
public sealed class ExtensionSettingsSection : ISettingsSection
{
    private const string NotSet = "not set";
    private readonly IOptions<ExtensionOptions> _options;

    public ExtensionSettingsSection(IOptions<ExtensionOptions> options)
    {
        _options = options;
    }

    public IReadOnlyList<SettingValue> Read() =>
    [
        new("GEMINARR__EXTENSION__TOKENFILE", string.IsNullOrWhiteSpace(_options.Value.TokenFile) ? NotSet : "set", true),
        new("GEMINARR__EXTENSION__PACKAGEFOLDER", _options.Value.PackageFolder, false),
        new("GEMINARR__EXTENSION__PUBLICURL", string.IsNullOrWhiteSpace(_options.Value.PublicUrl) ? NotSet : _options.Value.PublicUrl, false),
        new("GEMINARR__EXTENSION__SILENTAFTERMINUTES", _options.Value.SilentAfterMinutes.ToString(CultureInfo.InvariantCulture), false),
    ];
}

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
