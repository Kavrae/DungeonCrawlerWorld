using Builders.Editing;
using Engine.Settings;
using Game.Bootstrap;
using Game.Content;
using Game.World;
using Workshop.Editing;

namespace DungeonCrawlerWorld.Workshop;

/// <summary>Every game build the Workshop makes over the source tree's master: its session, each rebuild after a save, the Map tab's previews, and a publish's trial builds.</summary>
/// <remarks>
/// Mods are validated against master each time the session is built, so a mod whose records a publish broke is dropped
/// (and listed) on the next rebuild; previews use the mods the latest build validated. A rebuild runs on a worker, so the
/// latest mods are handed across with a volatile write.
/// </remarks>
internal sealed class WorkshopContentBuilds(string modsDirectory, string masterDirectory, string draftsDirectory, IReadOnlyList<ISettingsSource> settingsSources, TextWriter failureLog)
{
    private volatile ValidatedMods? _validatedMods;

    /// <summary>The session over master as saved now, its mods validated against it -- the first one, and each rebuild (on a worker).</summary>
    public GameSession BuildSession()
    {
        var content = GameContent.MasterOnly(masterDirectory);
        var validatedMods = ContentSession.ValidateMods(modsDirectory, content, settingsSources);
        _validatedMods = validatedMods;
        return ContentSession.Build(validatedMods, content, settingsSources, failureLog);
    }

    /// <summary>A session over map with the mods the latest build validated -- what the Map tab builds each preview on.</summary>
    public GameSession BuildPreview(Map map)
    {
        var validatedMods = _validatedMods ?? throw new InvalidOperationException("A preview is built only once the session has been.");
        return ContentSession.BuildPreview(validatedMods, GameContent.MasterOnly(masterDirectory), map, settingsSources);
    }

    /// <summary>How the workspace saves into master and Drafts, tries a publish and rebuilds.</summary>
    public ContentSaving Saving() => new(
        tableName => new ContentTableFiles(tableName, Path.Combine(masterDirectory, GameContent.FileNameOf(tableName)), Path.Combine(draftsDirectory, GameContent.FileNameOf(tableName))),
        () => ContentSession.ReadMasterFiles(masterDirectory),
        files => ContentSession.TrialBuild(files, settingsSources),
        BuildSession);
}
