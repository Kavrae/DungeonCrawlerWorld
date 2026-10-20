using Engine.ECS.Context;
using Engine.Modules;
using Engine.Settings;

namespace Engine.Bootstrap;

/// <summary>Everything EcsBuilder.Build needs to turn a module set into a running EcsContext: the modules, the policy the build runs under, and the game's callbacks at fixed points of the build.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed record EcsBuildRequest<TContext>
{
    /// <summary>What the build is for; only a Session build can begin a session. CreateContext sees it (EcsBuildServices.Purpose) and so do the other callbacks (EcsContext.Purpose).</summary>
    public EcsBuildPurpose Purpose { get; init; } = EcsBuildPurpose.Session;

    /// <summary>The game's own modules, in their default order.</summary>
    public required IReadOnlyList<ModuleFactory<TContext>> Modules { get; init; }

    /// <summary>Mod modules: each replaces the module sharing its Id, or is added after the others.</summary>
    public IReadOnlyList<ModuleFactory<TContext>> Mods { get; init; } = [];

    /// <summary>Where setting overrides come from, in order.</summary>
    public IReadOnlyList<ISettingsSource> SettingsSources { get; init; } = [];

    /// <summary>The host's fixed update rate, applied to GameTiming before any module runs.</summary>
    public required ushort FramesPerSecond { get; init; }

    public required int InitialEntityCapacity { get; init; }

    /// <summary>The dense storage a Packed or Multi pool starts with when its registration gives none.</summary>
    public required int InitialComponentCapacity { get; init; }

    /// <summary>Modules the build itself needs, whatever the modules require of each other -- the ones CreateContext reads pools from.</summary>
    public IReadOnlyList<Guid> RequiredModuleIds { get; init; } = [];

    /// <summary>How many processing tiers, from the lowest index up, every ITieredSystem simulates. See SystemManager.SimulatedTierCount.</summary>
    public int SimulatedTierCount { get; init; } = int.MaxValue;

    /// <summary>Builds the context every module is configured with, once every component is registered.</summary>
    public required Func<EcsBuildServices, TContext> CreateContext { get; init; }

    /// <summary>Runs after every module's Configure and before any RegisterBehavior: the game's checks on what the modules configured.</summary>
    public Action<TContext>? AfterConfigure { get; init; }

    /// <summary>Runs after every module's RegisterBehavior, before the build returns: the game's own systems, handlers and diagnostics policy.</summary>
    public Action<TContext, EcsContext>? AfterBehavior { get; init; }
}
