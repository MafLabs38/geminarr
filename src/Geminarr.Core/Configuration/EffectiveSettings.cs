using Geminarr.Core.Access;
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
    private readonly IOptions<GeminarrOptions> _geminarr;
    private readonly IOptions<AuthOptions> _auth;
    private readonly IOperatorCredentials _credentials;

    public EffectiveSettings(IOptions<GeminarrOptions> geminarr, IOptions<AuthOptions> auth, IOperatorCredentials credentials)
    {
        _geminarr = geminarr;
        _auth = auth;
        _credentials = credentials;
    }

    public IReadOnlyList<SettingValue> Read() =>
    [
        new("GEMINARR__DATAFOLDER", _geminarr.Value.DataFolder, false),
        new("GEMINARR__PUBLICURL", _geminarr.Value.PublicUrl ?? "not set", false),
        new("GEMINARR__ALLOWEDHOSTNAMES", _geminarr.Value.AllowedHostNames, false),
        new("GEMINARR__AUTH__ADMINUSER", _credentials.Name is null ? "not set" : "set", true),
        new("GEMINARR__AUTH__ADMINPASSWORDFILE", string.IsNullOrWhiteSpace(_auth.Value.AdminPasswordFile) ? "not set" : "set", true),
        new("GEMINARR__AUTH__COOKIESECURITY", _auth.Value.CookieSecurity.ToString(), false),
        new("GEMINARR__AUTH__SESSIONHOURS", _auth.Value.SessionHours.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
    ];
}
