using Geminarr.Core.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Geminarr.Api.V1.Settings;

public sealed record SettingResource(string Name, string Value, bool Secret);

public sealed record SettingsResource(IReadOnlyList<SettingResource> Settings);

[ApiController]
[Route("api/v1/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly EffectiveSettings _settings;

    public SettingsController(EffectiveSettings settings)
    {
        _settings = settings;
    }

    /// <summary>The settings the container runs with, read only (they are changed in its manifest). A secret is shown as "set" or "not set", never its value or its path.</summary>
    [HttpGet]
    [ProducesResponseType<SettingsResource>(StatusCodes.Status200OK)]
    public SettingsResource Get() => new([.. _settings.Read().Select(setting => new SettingResource(setting.Name, setting.Value, setting.Secret))]);
}
