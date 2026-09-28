using Engine.Modules;

namespace Game.Modules;

/// <summary>A module of the game: configured with, and registering its systems against, the GameModuleContext every module in one build shares.</summary>
/// <remarks>
/// GameBuildPass runs each phase for every module before the next: DeclareSettings, RegisterComponents,
/// Configure, then RegisterSystems. A mod implements this, or IModule&lt;GameModuleContext&gt; directly.
/// </remarks>
public interface IGameModule : IModule<GameModuleContext>;
