using Geminarr.Core.Link;

namespace Geminarr.Api.V1.Mnemarr;

/// <summary>The destination: the address, the machine, whether it is linked, what is wrong, and what waits. The key is never in it.</summary>
public sealed record MnemarrResource(string? Url, string Machine, bool Linked, bool AddressFromSettings, string? Problem, DateTimeOffset? LastHeartbeatAt, long PendingLines)
{
    public static MnemarrResource From(DestinationStatus status, long pendingLines) =>
        new(status.Url, status.Machine, status.Linked, status.AddressFromSettings, status.Problem, status.LastHeartbeatAt, pendingLines);
}

/// <summary>The address of the Mnemarr to send to.</summary>
public sealed record MnemarrAddressRequest(string? Url);

/// <summary>A pairing: <c>pending</c> with the code to show and the page where a person approves it (Mnemarr's own, host visible), or how it ended.</summary>
public sealed record PairingResource(string State, string? UserCode, string? VerificationUrl, DateTimeOffset? ExpiresAt)
{
    public static PairingResource From(PairingStatus status) => new(status.State, status.UserCode, status.VerificationUrl, status.ExpiresAt);
}
