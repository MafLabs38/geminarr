namespace Geminarr.Core.Configuration;

/// <summary>The settings of one part of the application, for the page of the operator: each part says its own, so that a part that is added does not widen the constructor of <see cref="EffectiveSettings"/>. The older parts stay where they are, on purpose: moving them is not what this is for.</summary>
public interface ISettingsSection
{
    IReadOnlyList<SettingValue> Read();
}
