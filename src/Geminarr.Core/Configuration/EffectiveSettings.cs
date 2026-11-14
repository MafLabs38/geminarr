using Geminarr.Core.Access;
using Geminarr.Core.Browser;
using Geminarr.Core.Identity;
using Geminarr.Core.Link;
using Geminarr.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Configuration;

/// <summary>One setting as the container runs with it: the name of the variable, its value (or "set" / "not set" for a secret), and whether it is a secret.</summary>
public sealed record SettingValue(string Name, string Value, bool Secret);

/// <summary>
/// The settings the container runs with, for the page of the operator. A secret is never shown, and neither is the path of its file (the password
/// file says only whether it is there). The list grows with the verticals; a setting that is added is added here in the same change.
/// </summary>
public sealed class EffectiveSettings
{
    private const string NotSet = "not set";

    private readonly IOptions<GeminarrOptions> _geminarr;
    private readonly IOptions<AuthOptions> _auth;
    private readonly IOperatorCredentials _credentials;
    private readonly IOptions<IdentityOptions> _identity;
    private readonly IOptions<MnemarrOptions> _mnemarr;
    private readonly PassSettings _pass;
    private readonly IOptions<BrowserOptions> _browser;

    public EffectiveSettings(
        IOptions<GeminarrOptions> geminarr,
        IOptions<AuthOptions> auth,
        IOperatorCredentials credentials,
        IOptions<IdentityOptions> identity,
        IOptions<MnemarrOptions> mnemarr,
        IOptions<BrowserOptions> browser,
        PassSettings pass)
    {
        _browser = browser;
        _pass = pass;
        _geminarr = geminarr;
        _auth = auth;
        _credentials = credentials;
        _identity = identity;
        _mnemarr = mnemarr;
    }

    public IReadOnlyList<SettingValue> Read() =>
    [
        new("GEMINARR__DATAFOLDER", _geminarr.Value.DataFolder, false),
        new("GEMINARR__PUBLICURL", _geminarr.Value.PublicUrl ?? NotSet, false),
        new("GEMINARR__ALLOWEDHOSTNAMES", _geminarr.Value.AllowedHostNames, false),
        new("GEMINARR__AUTH__ADMINUSER", _credentials.Name is null ? NotSet : "set", true),
        new("GEMINARR__AUTH__ADMINPASSWORDFILE", string.IsNullOrWhiteSpace(_auth.Value.AdminPasswordFile) ? NotSet : "set", true),
        new("GEMINARR__AUTH__COOKIESECURITY", _auth.Value.CookieSecurity.ToString(), false),
        new("GEMINARR__AUTH__SESSIONHOURS", _auth.Value.SessionHours.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__IDENTITY__NAME", _identity.Value.Name is { Length: > 0 } name ? name : NotSet, false),
        new("GEMINARR__IDENTITY__LOGOFILE", string.IsNullOrWhiteSpace(_identity.Value.LogoFile) ? NotSet : "set", false),
        new("GEMINARR__IDENTITY__COLORS__NAME", _identity.Value.Colors.Name is { Length: > 0 } nameColor ? nameColor : NotSet, false),
        new("GEMINARR__IDENTITY__COLORS__VERSION", _identity.Value.Colors.Version is { Length: > 0 } versionColor ? versionColor : NotSet, false),
        new("GEMINARR__MNEMARR__URL", string.IsNullOrWhiteSpace(_mnemarr.Value.Url) ? NotSet : _mnemarr.Value.Url, false),
        new("GEMINARR__MNEMARR__MACHINE", _mnemarr.Value.Machine, false),
        new("GEMINARR__MNEMARR__KEYFILE", string.IsNullOrWhiteSpace(_mnemarr.Value.KeyFile) ? "default" : "set", false),
        new("GEMINARR__MNEMARR__ENROLMENTCODEFILE", string.IsNullOrWhiteSpace(_mnemarr.Value.EnrolmentCodeFile) ? NotSet : "set", false),
        new("GEMINARR__MNEMARR__ALLOWINSECUREHTTP", _mnemarr.Value.AllowInsecureHttp ? "true" : "false", false),
        new("GEMINARR__READER__KIND", _pass.Reading.Kind.ToString(), false),
        new("GEMINARR__READER__POLLMINUTES", _pass.Reading.PollMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__MAXLISTED", _pass.Reading.MaxListed.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__PASSTIMEOUTMINUTES", _pass.Reading.PassTimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__MAXBACKOFFMINUTES", _pass.Reading.MaxBackoffMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__LOGINFAILURES", _pass.Reading.LoginFailures.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__DEMOGROWSECONDS", _pass.Reading.DemoGrowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__READER__MAXOPENPERPASS", _pass.Reading.MaxOpenPerPass.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__BROWSER__PROFILEFOLDER", "set", false),
        new("GEMINARR__BROWSER__ASSISTANTURL", _browser.Value.AssistantUrl, false),
        new("GEMINARR__BROWSER__LIVEVIEWMINUTES", _browser.Value.LiveViewMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        new("GEMINARR__BROWSER__ALLOWEDHOSTS", _browser.Value.AllowedHosts, false),
        new("GEMINARR__BROWSER__SIGNINHOSTS", _browser.Value.SignInHosts, false),
        new("GEMINARR__BROWSER__SIGNINDETECTION", _browser.Value.SignInDetection.ToString(), false),
        new("GEMINARR__BROWSER__CHROMIUMSANDBOX", _browser.Value.ChromiumSandbox ? "true" : "false", false),
        new("GEMINARR__BROWSER__ALLOWINSECUREASSISTANT", _browser.Value.AllowInsecureAssistant ? "true" : "false", false),
        new("GEMINARR__RETENTION__RUNSDAYS", _pass.RunsDays.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
    ];
}
