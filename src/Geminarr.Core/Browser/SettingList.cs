namespace Geminarr.Core.Browser;

/// <summary>A list in a setting, as the settings write it: entries separated by <c>;</c>, <c>,</c> or a space.</summary>
public static class SettingList
{
    public static string[] Entries(string list) => list.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
