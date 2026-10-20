using Engine.Content;
using Engine.ECS.Components;
using Engine.Settings;
using Engine.Tags;

namespace Engine.Modules;

/// <summary>What a module's RegisterComponents phase can reach.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ComponentRegistration(ComponentManager componentManager, ModuleDeclarations declarations)
{
    public ComponentManager ComponentManager { get; } = componentManager;

    /// <summary>Everything the build's modules declared.</summary>
    public ModuleDeclarations Declarations { get; } = declarations;

    public SettingValues Settings => Declarations.Settings;

    public GameplayTagRegistry Tags => Declarations.Tags;

    public ContentTypeRegistry ContentTypes => Declarations.ContentTypes;
}
