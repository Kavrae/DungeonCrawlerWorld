using Engine.Math;
using Engine.Modules;
using Engine.Tags;
using Game.Modules.Core.Components;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Game.Modules.Core;

/// <summary>Shared components reused across other modules: Transform, DisplayText, Glyph, Sprite, Background, ActionLock.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class CoreModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000001");

    public Guid Id => ModuleId;

    /// <summary>Declares every built-in gameplay tag.</summary>
    /// <remarks>Declared here, on a module every build has, rather than by each module that uses a tag: a tag shared between modules would otherwise make one require the other.</remarks>
    public void DeclareTags(GameplayTagDeclarations tags)
    {
        foreach (var tag in GameTags.All)
        {
            tags.Declare(tag);
        }
    }

    /// <summary>Nothing to configure -- kept because IGameModule requires it, and because Core is still a game module by every other measure.</summary>
    public void Configure(GameModuleContext context)
    {
    }

    /// <summary>Registers core components with their appropriate component pools.</summary>
    /// <remarks>For each component type, defines the merge action for combining two of those components.</remarks>
    /// <param name="componentManager"></param>
    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;


        componentManager.RegisterPackedPool<BackgroundComponent>(static (ref existing, incoming) =>
        {
            existing.BackgroundColor = Color.Lerp(existing.BackgroundColor, incoming.BackgroundColor, 0.5f);
        });

        // Rare: an entity is named and drawn by its blueprint's appearance (see EntityNaming and
        // EntityAppearance), so only one that differs from it -- a destroyed container -- holds any
        // of the three below.
        componentManager.RegisterPackedPool<DisplayTextComponent>(static (ref existing, incoming) =>
        {
            existing.Name = existing.Name + " " + incoming.Name;
            existing.Description = existing.Description + Environment.NewLine + incoming.Description;
        }, initialCapacity: 64);

        componentManager.RegisterPackedPool<GlyphComponent>(static (ref existing, incoming) =>
        {
            existing.GlyphColor = Color.Lerp(existing.GlyphColor, incoming.GlyphColor, 0.5f);
        }, initialCapacity: 64);

        componentManager.RegisterPackedPool<SpriteComponent>(static (ref existing, incoming) => { }, initialCapacity: 64);

        // Rough estimate of potential non-blocking entity population.
        componentManager.RegisterMultiPool<NonBlockingComponent>(initialCapacity: 40_000);

        // Always very rare -- start small, grow on demand.
        componentManager.RegisterMultiPool<ForceBlockingComponent>(initialCapacity: 16);

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

            // The later deadline wins rather than averaging: averaging two absolute frames would
            // invent a moment neither part asked for, and a merge must never shorten a lock that
            // is already running.
            existing.UnlockedAtFrame = System.Math.Max(existing.UnlockedAtFrame, incoming.UnlockedAtFrame);
        });
    }

    /// <summary>
    /// No systems of its own any more. ActionLockSystem used to count every entity's lock down
    /// here; the lock is a deadline now (see ActionLockComponent), so nothing has to visit an
    /// entity for it to become unlocked.
    /// </summary>
    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
    }
}