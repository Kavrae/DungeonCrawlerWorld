using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Modules;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Core;

/// <summary>Shared components reused across other modules: Transform, DisplayText, Glyph, Background.</summary>
public sealed class CoreModule : IModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000001");

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

        componentManager.RegisterPackedPool<OccupancyComponent>(static (ref existing, incoming) =>
        {
            existing.IsTiny |= incoming.IsTiny;
            existing.IsPhasing |= incoming.IsPhasing;
        });

        componentManager.RegisterMultiPool<NonBlockingComponent>();
        componentManager.RegisterMultiPool<ForceBlockingComponent>();

        componentManager.RegisterDirectPool<TransformComponent>(static (ref existing, incoming) =>
        {
            existing.Size = new Vector2Byte(
                (byte)((existing.Size.X + incoming.Size.X) / 2),
                (byte)((existing.Size.Y + incoming.Size.Y) / 2));
        });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // No systems of its own -- Core only provides shared component types other modules build on.
    }
}