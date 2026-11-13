using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Link;

/// <summary>The Mnemarr that Geminarr sends to, from the <c>Mnemarr</c> section (SPEC §5.1, §10.2).</summary>
public sealed class MnemarrOptions
{
    public const string SectionName = "Mnemarr";

    public const string DefaultMachine = "gemini";

    /// <summary>The address of the Mnemarr (https). When it is not set here, the operator gives it on the page of Mnemarr, and it is kept in the database.</summary>
    public string? Url { get; set; }

    /// <summary>The name of this machine in Mnemarr. A pairing lets the person who approves it name the machine, and that name wins.</summary>
    public string Machine { get; set; } = DefaultMachine;

    /// <summary>Where the key of the machine is kept: a file only the container's user can read. <c>&lt;data&gt;/mnemarr/key</c> when empty.</summary>
    public string? KeyFile { get; set; }

    /// <summary>A file with a one-time code (enrolment, for a deployment as code): exchanged for the key at start, when there is no key yet.</summary>
    public string? EnrolmentCodeFile { get; set; }

    /// <summary>A sandbox only: talk to Mnemarr over plain http to a host that is not the loopback.</summary>
    public bool AllowInsecureHttp { get; set; }
}

public sealed partial class MnemarrOptionsValidator : IValidateOptions<MnemarrOptions>
{
    /// <summary>Whether a machine name is one this version accepts: 1 to 40 lower-case letters, digits or hyphens, starting and ending with a letter or a digit. One rule, for the setting and for what Mnemarr gives back.</summary>
    public static bool IsValidMachine(string? machine) => machine is not null && MachinePattern().IsMatch(machine);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$")]
    private static partial Regex MachinePattern();

    public ValidateOptionsResult Validate(string? name, MnemarrOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Url) && MnemarrAddress.Normalize(options.Url) is null)
        {
            return ValidateOptionsResult.Fail("Mnemarr:Url (GEMINARR__MNEMARR__URL) must be an http or https address with a host, no user name and no path beyond a slash, for example https://mnemarr.homelab.example.");
        }

        return IsValidMachine(options.Machine)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Mnemarr:Machine (GEMINARR__MNEMARR__MACHINE) must be 1 to 40 lower-case letters, digits or hyphens, starting and ending with a letter or a digit.");
    }
}
