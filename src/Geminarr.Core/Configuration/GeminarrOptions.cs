namespace Geminarr.Core.Configuration;

/// <summary>Settings read from appsettings.json, then from environment variables prefixed with <c>GEMINARR__</c>.</summary>
public sealed class GeminarrOptions
{
    /// <summary>Folder that holds the database and the settings. Mount it as a volume. The browser profile (the signed-in session) has a folder of its own (SPEC §7.1).</summary>
    public string DataFolder { get; set; } = "/config";

    /// <summary>
    /// The names the page may be reached by, separated by commas or semicolons (a port is ignored): a request for any other <c>Host</c> is refused, so that a web page
    /// that points its own name at this address (DNS rebinding) cannot read what this container says. The probes are exempt (a platform calls them by address). The host of
    /// <see cref="PublicUrl"/> is allowed as well. <c>*</c> allows every name (not recommended).
    /// </summary>
    public string AllowedHostNames { get; set; } = "localhost;127.0.0.1;[::1]";

    /// <summary>The address of the page as the operator reaches it (for example <c>https://geminarr.homelab.example</c>): its host is allowed, and it is the address that goes in the link of a request for a sign-in.</summary>
    public string? PublicUrl { get; set; }
}
