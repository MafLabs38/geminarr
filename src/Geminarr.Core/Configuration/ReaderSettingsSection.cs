using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Configuration;

/// <summary>The settings of the reader, for the page of the operator.</summary>
public sealed class ReaderSettingsSection : ISettingsSection
{
    private readonly IOptions<ReaderOptions> _reader;

    public ReaderSettingsSection(IOptions<ReaderOptions> reader)
    {
        _reader = reader;
    }

    public IReadOnlyList<SettingValue> Read() =>
    [
        new("GEMINARR__READER__KIND", _reader.Value.Kind.ToString(), false),
        new("GEMINARR__READER__POLLMINUTES", _reader.Value.PollMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__MAXLISTED", _reader.Value.MaxListed.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__PASSTIMEOUTMINUTES", _reader.Value.PassTimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__MAXBACKOFFMINUTES", _reader.Value.MaxBackoffMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__LOGINFAILURES", _reader.Value.LoginFailures.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__DEMOGROWSECONDS", _reader.Value.DemoGrowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__MAXOPENPERPASS", _reader.Value.MaxOpenPerPass.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__BACKFILLPERPASS", _reader.Value.BackfillPerPass.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
    ];
}
