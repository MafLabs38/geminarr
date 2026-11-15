using Geminarr.Core.Browser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geminarr.Browser;

public static class BrowserServiceCollectionExtensions
{
    /// <summary>The browser of the host is the browser container, reached through its debugging address (ADR 0016). Nothing is attached until a session is checked.</summary>
    public static IServiceCollection AddGeminarrBrowser(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IBrowserHost, CdpBrowserHost>());
        return services;
    }
}
