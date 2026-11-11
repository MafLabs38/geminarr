using Geminarr.Api.V1.SystemInfo;
using Microsoft.Extensions.DependencyInjection;

namespace Geminarr.Api.V1;

public static class ApiV1ServiceCollectionExtensions
{
    /// <summary>Registers the controllers of <c>/api/v1</c>, found in this assembly.</summary>
    public static IServiceCollection AddGeminarrApiV1(this IServiceCollection services)
    {
        services.AddControllers().AddApplicationPart(typeof(SystemController).Assembly);
        return services;
    }
}
