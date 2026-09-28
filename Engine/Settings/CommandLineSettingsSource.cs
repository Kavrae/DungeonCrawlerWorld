namespace Engine.Settings;

/// <summary>Setting overrides from command-line arguments of the form "--setting=ModuleName.SettingName=Value".</summary>
/// <remarks>Arguments without the "--setting=" prefix are ignored. The value is everything after the second '=', so it may contain '='.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class CommandLineSettingsSource : ISettingsSource
{
    public const string Prefix = "--setting=";

    public CommandLineSettingsSource(IEnumerable<string> arguments)
    {
        var overrides = new List<SettingOverride>();
        var malformedEntries = new List<string>();

        foreach (var argument in arguments)
        {
            if (!argument.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (TryParse(argument[Prefix.Length..], out var settingOverride))
            {
                overrides.Add(settingOverride);
            }
            else
            {
                malformedEntries.Add(argument);
            }
        }

        Overrides = overrides;
        MalformedEntries = malformedEntries;
    }

    public string Name => "command line";

    public IReadOnlyList<SettingOverride> Overrides { get; }

    public IReadOnlyList<string> MalformedEntries { get; }

    private static bool TryParse(string assignment, out SettingOverride settingOverride)
    {
        settingOverride = default;

        var equalsIndex = assignment.IndexOf('=');
        if (equalsIndex <= 0)
        {
            return false;
        }

        var qualifiedName = assignment[..equalsIndex];
        var dotIndex = qualifiedName.IndexOf('.');
        if (dotIndex <= 0 || dotIndex == qualifiedName.Length - 1)
        {
            return false;
        }

        settingOverride = new SettingOverride(qualifiedName[..dotIndex], qualifiedName[(dotIndex + 1)..], assignment[(equalsIndex + 1)..]);
        return true;
    }
}
