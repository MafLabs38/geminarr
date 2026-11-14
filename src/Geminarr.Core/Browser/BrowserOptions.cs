using Microsoft.Extensions.Options;

namespace Geminarr.Core.Browser;

/// <summary>How the end of a sign-in is known.</summary>
public enum SignInDetection
{
    /// <summary>The operator says so (a button of the live view), and the browser must be on the assistant's own page. The default: a page of the assistant that is shown to somebody who is not signed in (a guest mode) cannot be told from one that is by its address.</summary>
    Confirm,

    /// <summary>The browser being on the assistant's own page (not a sign-in page) is enough: for an assistant that sends everybody who is not signed in to a sign-in page, and for the tests.</summary>
    Page,
}

/// <summary>The browser that holds the signed-in session of the assistant, from the <c>Browser</c> section (SPEC §7, D-08).</summary>
public sealed class BrowserOptions
{
    public const string SectionName = "Browser";

    /// <summary>The folder of the browser profile: the signed-in session. A volume of its own, mode 0700, never in a backup in the clear.</summary>
    public string ProfileFolder { get; set; } = "/profile";

    /// <summary>The page of the assistant that is opened; its host is the origin the browser may talk to.</summary>
    public string AssistantUrl { get; set; } = "https://gemini.google.com/app";

    /// <summary>The sign-in pages of the account provider: the only other origin the browser may load, and only during a live view.</summary>
    public string SignInHosts { get; set; } = "accounts.google.com";

    /// <summary>
    /// The hosts the assistant's own page loads its scripts, styles, fonts and pictures from (a name, or <c>*.suffix</c>), besides its own host; never a page to go to. Names, not the whole of a domain that
    /// serves what anybody uploads (<c>storage.googleapis.com</c>, the pictures of <c>googleusercontent.com</c> are listed one by one). The first spike writes down the exact list.
    /// </summary>
    public string AllowedHosts { get; set; } = "*.gstatic.com;lh3.googleusercontent.com;lh4.googleusercontent.com;lh5.googleusercontent.com;lh6.googleusercontent.com;fonts.googleapis.com;www.googleapis.com;apis.google.com";

    /// <summary>How a sign-in is known to be done: <see cref="SignInDetection.Confirm"/> by default.</summary>
    public SignInDetection SignInDetection { get; set; } = SignInDetection.Confirm;

    /// <summary>A sandbox or a test only: the assistant, its hosts and the sign-in pages may be reached over plain http.</summary>
    public bool AllowInsecureAssistant { get; set; }

    /// <summary>
    /// Whether Chromium runs with its own sandbox (the process of a page is cut off from the rest of the container). Off by default: the sandbox needs user namespaces or a relaxed seccomp profile, which a
    /// hardened container does not give, and Chromium then does not start at all (ADR 0015, SECURITY.md). On wherever the platform allows it.
    /// </summary>
    public bool ChromiumSandbox { get; set; }

    /// <summary>The path of a Chromium to use instead of the one that Playwright installed.</summary>
    public string? ExecutablePath { get; set; }

    public int ViewportWidth { get; set; } = 1280;

    public int ViewportHeight { get; set; } = 800;

    /// <summary>A live view ends after this many minutes.</summary>
    public int LiveViewMinutes { get; set; } = 10;

    /// <summary>The quality (1 to 100) of the pictures of the live view.</summary>
    public int ScreencastQuality { get; set; } = 60;

    /// <summary>How long the page of the assistant may take to load when the session is checked.</summary>
    public int SessionCheckTimeoutSeconds { get; set; } = 30;
}

public sealed class BrowserOptionsValidator : IValidateOptions<BrowserOptions>
{
    public ValidateOptionsResult Validate(string? name, BrowserOptions options)
    {
        var problem =
            (string.IsNullOrWhiteSpace(options.ProfileFolder) ? "Browser:ProfileFolder (GEMINARR__BROWSER__PROFILEFOLDER) must name the folder of the profile." : null)
            ?? (OriginPolicy.AssistantHost(options) is null ? "Browser:AssistantUrl (GEMINARR__BROWSER__ASSISTANTURL) must be an https address with a host (http only when the sandbox switch allows it)." : null)
            ?? Range("ViewportWidth", options.ViewportWidth, 320, 3840)
            ?? Range("ViewportHeight", options.ViewportHeight, 240, 2160)
            ?? Range("LiveViewMinutes", options.LiveViewMinutes, 1, 60)
            ?? Range("ScreencastQuality", options.ScreencastQuality, 1, 100)
            ?? Range("SessionCheckTimeoutSeconds", options.SessionCheckTimeoutSeconds, 5, 300);
        return problem is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problem);
    }

    private static string? Range(string setting, int value, int min, int max) =>
        value >= min && value <= max ? null : $"Browser:{setting} (GEMINARR__BROWSER__{setting.ToUpperInvariant()}) must be between {min} and {max}.";
}
