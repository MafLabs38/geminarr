using Microsoft.Extensions.Options;

namespace Geminarr.Core.Access;

/// <summary>Whether the session cookie is sent over HTTPS only.</summary>
public enum CookieSecurityMode
{
    /// <summary>The default: the cookie is <c>Secure</c>.</summary>
    Always,

    /// <summary>For a plain-HTTP sandbox: the cookie is <c>Secure</c> only if the request was.</summary>
    SameAsRequest,
}

/// <summary>Settings of the access control, from the <c>Auth</c> section (see docs/configuration.md). One operator: there is no account to manage.</summary>
public sealed class AuthOptions
{
    public const int MaxSessionHours = 720;

    /// <summary>The name of the operator. Choose one that is not obvious: whoever knows it can lock it out for fifteen minutes (five wrong passwords).</summary>
    public string? AdminUser { get; set; }

    /// <summary>The file that holds the password of the operator (a secret mounted as a file, never an environment variable). It is read at each sign-in, so it can be rotated without a restart.</summary>
    public string? AdminPasswordFile { get; set; }

    /// <summary>The cookie is <c>Secure</c> unless told otherwise (a sandbox in plain HTTP).</summary>
    public CookieSecurityMode CookieSecurity { get; set; } = CookieSecurityMode.Always;

    /// <summary>How long a session lasts, in hours, used or not: the operator signs in again after it.</summary>
    public int SessionHours { get; set; } = 12;
}

public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        if (options.SessionHours is < 1 or > AuthOptions.MaxSessionHours)
        {
            return ValidateOptionsResult.Fail($"Auth:SessionHours (GEMINARR__AUTH__SESSIONHOURS) must be between 1 and {AuthOptions.MaxSessionHours}.");
        }

        return string.IsNullOrWhiteSpace(options.AdminUser) == string.IsNullOrWhiteSpace(options.AdminPasswordFile)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Auth:AdminUser and Auth:AdminPasswordFile (GEMINARR__AUTH__ADMINUSER, GEMINARR__AUTH__ADMINPASSWORDFILE) are set together or not at all.");
    }
}
