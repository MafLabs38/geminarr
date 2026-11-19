using System.Globalization;
using Geminarr.Core.Extension;
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
        new("GEMINARR__EXTENSION__ROUNDMINUTES", _options.Value.RoundMinutes.ToString(CultureInfo.InvariantCulture), false),
    ];
}
