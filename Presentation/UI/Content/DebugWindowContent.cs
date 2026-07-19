using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using FontStashSharp;
using Game.Modules.Movement.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;

namespace Presentation.UI.Content;

/// <summary>
/// Admin-only entity-count/FPS-UPS debug readout. Rate counting is delegated to
/// Engine/Diagnostics's PerformanceCounter rather than rolled inline, and entity/component
/// counts are read live off EntityManager/ComponentManager.
/// </summary>
public sealed class DebugWindowContent(
    FontService fontService,
    EntityManager entityManager,
    ComponentManager componentManager) : IWindowContent
{
    private static readonly Vector2 DrawsPerSecondOffset = new(60, 0);
    private static readonly Vector2 EntityCountOffset = new(120, 0);
    private static readonly Vector2 MovingEntityCountOffset = new(240, 0);

    private readonly PerformanceCounter _updateCounter = new();
    private readonly PerformanceCounter _drawCounter = new();

    private SpriteFontBase _font = null!;

    private string _updatesPerSecondText = string.Empty;
    private string _drawsPerSecondText = string.Empty;
    private string _entityCountText = string.Empty;
    private string _movingEntityCountText = string.Empty;

    public void Initialize(Window hostWindow)
    {
        _font = fontService.GetFont(8);
    }

    public void Update(GameTime gameTime)
    {
        _updateCounter.Tick();
        _updatesPerSecondText = $"{_updateCounter.RatePerSecond:N1} ups";

        _entityCountText = $"Entities : {entityManager.LivingEntityCount:N0}";
        _movingEntityCountText = $"Moving Entities : {componentManager.GetPackedPool<MovementComponent>().Count:N0}";
    }

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        _drawCounter.Tick();
        _drawsPerSecondText = $"{_drawCounter.RatePerSecond:N1} fps";

        var rateColor = gameTime.IsRunningSlowly ? Color.Red : Color.Black;
        spriteBatch.DrawString(_font, _updatesPerSecondText, Vector2.Zero, rateColor);
        spriteBatch.DrawString(_font, _drawsPerSecondText, DrawsPerSecondOffset, rateColor);
        spriteBatch.DrawString(_font, _entityCountText, EntityCountOffset, Color.Black);
        spriteBatch.DrawString(_font, _movingEntityCountText, MovingEntityCountOffset, Color.Black);
    }
}
