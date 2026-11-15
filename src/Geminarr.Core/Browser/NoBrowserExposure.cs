namespace Geminarr.Core.Browser;

/// <summary>Nothing is known to be exposed: the default where the namespace cannot be read (a host that is not Linux, a test).</summary>
public sealed class NoBrowserExposure : IBrowserExposure
{
    public ExposureReport Check() => ExposureReport.None;
}
