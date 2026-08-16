using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Core.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Core;

/// <summary>Shared components reused across other modules: Transform, DisplayText, Glyph, Sprite, Background, ActionLock.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class CoreModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000001");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private ProcessingTierEvents _processingTierEvents = null!;

    public void Configure(GameModuleContext context) => _processingTierEvents = context.ProcessingTierEvents;

    /// <summary>Registers core components with their appropriate component pools.</summary>
    /// <remarks>For each component type, defines the merge action for combining two of those components.</remarks>
    /// <param name="componentManager"></param>
    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterDirectPool<BackgroundComponent>(static (ref existing, incoming) =>
        {
            existing.BackgroundColor = Color.Lerp(existing.BackgroundColor, incoming.BackgroundColor, 0.5f);
        });

        componentManager.RegisterDirectPool<DisplayTextComponent>(static (ref existing, incoming) =>
        {
            existing.Name = existing.Name + " " + incoming.Name;
            existing.Description = existing.Description + Environment.NewLine + incoming.Description;
        });

        componentManager.RegisterDirectPool<GlyphComponent>(static (ref existing, incoming) =>
        {
            existing.GlyphColor = Color.Lerp(existing.GlyphColor, incoming.GlyphColor, 0.5f);
        });

        componentManager.RegisterDirectPool<SpriteComponent>(static (ref existing, incoming) => { });

        componentManager.RegisterMultiPool<NonBlockingComponent>();
        componentManager.RegisterMultiPool<ForceBlockingComponent>();

        componentManager.RegisterDirectPool<TransformComponent>(static (ref existing, incoming) =>
        {
            existing.Size = new Vector2Byte(
                (byte)((existing.Size.X + incoming.Size.X) / 2),
                (byte)((existing.Size.Y + incoming.Size.Y) / 2));
        });

        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) =>
        {
            existing.StandardLockFrames = MathUtility.ClampUShort(((existing.StandardLockFrames + incoming.StandardLockFrames) / 2), 0, ushort.MaxValue);
            existing.CurrentLockTotalFrames = MathUtility.ClampUShort(((existing.CurrentLockTotalFrames + incoming.CurrentLockTotalFrames) / 2), 0, ushort.MaxValue);
            existing.CurrentLockFramesRemaining = MathUtility.ClampUShort(((existing.CurrentLockFramesRemaining + incoming.CurrentLockFramesRemaining) / 2), 0, ushort.MaxValue);
        });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) =>
        systemManager.Register(new ActionLockSystem(
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents));
}