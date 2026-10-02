using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Modules;
using Engine.Settings;
using Game.Spawning;
using Game.World;

namespace Game.Bootstrap;

/// <summary>Everything a built game session offers the code outside Game: its ECS, World, catalogs, views and machinery.</summary>
/// <remarks>
/// Built once from the session's GameBuildPass, taking every member from its GameModuleContext by name, so a new
/// service is one property here rather than a positional field threaded through every bundle. The GameModuleContext
/// itself stays the modules' build-phase API and is not exposed.
/// </remarks>
public sealed class GameSession
{
    public GameSession(GameBuildPassResult buildPass, IReadOnlyList<ModuleFailure> moduleFailures, SpawnRecordRebuilder spawnRecordRebuilder, EntityTeleporter teleporter)
    {
        var context = buildPass.Context;

        EcsContext = buildPass.EcsContext;
        World = buildPass.World;
        Catalogs = new GameCatalogs(context);
        SimulationClock = context.SimulationClock;
        Views = new GameViews(buildPass.World, context, buildPass.LocalTierRoster);
        Commands = new GameCommands(buildPass.World, context);
        Internals = new GameSessionInternals(buildPass.Factory, spawnRecordRebuilder, teleporter, context.ProcessingTierResolver, buildPass.LocalTierRoster, context.MovedEntities, context.AuraField);
        ModuleFailures = moduleFailures;
        Settings = buildPass.Settings.Values;
        SettingsFailures = buildPass.Settings.Failures;
    }

    public EcsContext EcsContext { get; }

    public World.World World { get; }

    public GameCatalogs Catalogs { get; }

    /// <summary>The simulation's "now" -- the same instance SystemManager.Clock advances.</summary>
    public SimulationClock SimulationClock { get; }

    public GameViews Views { get; }

    public GameCommands Commands { get; }

    public GameSessionInternals Internals { get; }

    /// <summary>Every mod DLL or type that failed to load or validate.</summary>
    public IReadOnlyList<ModuleFailure> ModuleFailures { get; }

    /// <summary>Every setting the session's modules declared, resolved.</summary>
    public SettingValues Settings { get; }

    /// <summary>Every setting override that was rejected.</summary>
    public IReadOnlyList<SettingsFailure> SettingsFailures { get; }
}
