namespace Engine.Settings;

/// <summary>Somewhere setting overrides come from -- the command line today.</summary>
/// <cleanupVersion>1</cleanupVersion>
public interface ISettingsSource
{
    /// <summary>Names the source in a SettingsFailure.</summary>
    string Name { get; }

    /// <summary>Every override this source gives, in the order a later one replaces an earlier one.</summary>
    IReadOnlyList<SettingOverride> Overrides { get; }

    /// <summary>Entries this source could not read as an override at all, verbatim.</summary>
    IReadOnlyList<string> MalformedEntries { get; }
}

/// <summary>A value for one setting, named the way a player writes it: "ModuleName.SettingName".</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct SettingOverride(string ModuleName, string SettingName, string Value);
