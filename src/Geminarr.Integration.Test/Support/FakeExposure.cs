using Geminarr.Core.Browser;

namespace Geminarr.Integration.Test.Support;

/// <summary>What a test says the namespace shared with the browser shows: ports that listen off the loopback, or a listing that cannot be read.</summary>
public sealed class FakeExposure : IBrowserExposure
{
    public List<int> Ports { get; } = [];

    public bool Unreadable { get; set; }

    public ExposureReport Check() => new([.. Ports], Unreadable);
}
