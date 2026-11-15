using Geminarr.Core.Browser;

namespace Geminarr.Integration.Test.Support;

/// <summary>The ports that a test says listen off the loopback of the namespace shared with the browser.</summary>
public sealed class FakeExposure : IBrowserExposure
{
    public List<int> Ports { get; } = [];

    public IReadOnlyList<int> ExposedPorts() => Ports;
}
