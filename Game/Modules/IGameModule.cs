namespace Game.Modules;

/// <summary>
/// Optional extension for modules that need Game-layer runtime state (IMapQuery, MathUtility,
/// EventBus) not available at construction time. Engine.Modules.IModule stays Game-agnostic
/// -- it never references Game.World.World or anything else Game-specific -- so this lives
/// here instead of widening IModule's own signature. GameBootstrapper calls Configure once,
/// after construction but before RegisterComponents/RegisterSystems, for every module that
/// implements this (built-in or modded).
/// </summary>
public interface IGameModule : Engine.Modules.IModule
{
    void Configure(GameModuleContext context);
}