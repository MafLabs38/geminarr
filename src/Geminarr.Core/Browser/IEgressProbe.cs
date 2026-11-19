namespace Geminarr.Core.Browser;

/// <summary>
/// Whether Geminarr's own process is limited by the egress guard (ADR 0020): it tries a connection to an address that is on no list (a reserved one that no network routes), and the guard refuses it at once. Without the guard
/// the attempt does not come back refused (it waits, or finds no route). A guard that is missing fails open and nothing else would say so.
/// </summary>
public interface IEgressProbe
{
    /// <summary>True when a connection to an address that is on no list is refused at once, as the guard does.</summary>
    bool IsRefused();
}
