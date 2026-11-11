namespace Geminarr.Core.Configuration;

/// <summary>Settings read from appsettings.json, then from environment variables prefixed with <c>GEMINARR__</c>.</summary>
public sealed class GeminarrOptions
{
    /// <summary>Folder that holds the database and the settings. Mount it as a volume. The browser profile (the signed-in session) has a folder of its own (SPEC §7.1).</summary>
    public string DataFolder { get; set; } = "/config";
}
