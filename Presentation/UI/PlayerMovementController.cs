using Engine.ECS.Components.Stores;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.World;
using Microsoft.Xna.Framework.Input;

namespace Presentation.UI;

/// <summary>
/// Player's WASD movement input -- queues a move into MovementComponent.NextMapPosition for
/// MovementSystem to actually apply, gated by a fixed per-move cooldown. Split out from
/// ActionTargetingController (see that class's own doc comment): movement shares nothing with
/// the ability/item arm-target-confirm state machine beyond both being "what does this frame's
/// input do to the player entity" -- MapWindow.OnHotkeysAction calls both every frame, movement
/// first (matches this class's original per-frame hotkey ordering, from before the split).
/// </summary>
public sealed class PlayerMovementController(
    World world,
    DirectComponentPool<TransformComponent> transformPool,
    PackedComponentPool<MovementComponent> movementPool)
{
    private static readonly int FramesPerPlayerMove = GameTiming.FramesForSeconds(0.25f);

    private int _playerMoveCooldownFrames;

    public void HandleInput(KeyboardState keyboardState)
    {
        if (_playerMoveCooldownFrames > 0)
        {
            _playerMoveCooldownFrames--;
        }

        var delta = new Vector3Int();
        if (keyboardState.IsKeyDown(Keys.W))
        {
            delta.Y -= 1;
        }
        if (keyboardState.IsKeyDown(Keys.S))
        {
            delta.Y += 1;
        }
        if (keyboardState.IsKeyDown(Keys.A))
        {
            delta.X -= 1;
        }
        if (keyboardState.IsKeyDown(Keys.D))
        {
            delta.X += 1;
        }

        if (delta == new Vector3Int() || _playerMoveCooldownFrames > 0)
        {
            return;
        }

        _playerMoveCooldownFrames = FramesPerPlayerMove;
        TryQueuePlayerMove(delta);
    }

    private void TryQueuePlayerMove(Vector3Int delta)
    {
        var playerEntityId = world.PlayerEntityId;
        if (!transformPool.TryGetReadonly(playerEntityId, out var transformComponent) ||
            !movementPool.TryGetReadonly(playerEntityId, out var movementComponent))
        {
            return;
        }

        // Only queue a new move while at rest -- avoids redirecting a move that's already
        // pending (e.g. still waiting on MovementSystem's action lock).
        var isAtRest = movementComponent.NextMapPosition is null || movementComponent.NextMapPosition.Value == transformComponent.Position;
        if (!isAtRest)
        {
            return;
        }

        var candidate = transformComponent.Position + delta;
        var occupyingEntityId = world.GetEntityIdAt(candidate);
        if (!world.IsOnMap(candidate) || (occupyingEntityId != -1 && occupyingEntityId != playerEntityId))
        {
            return;
        }

        movementPool.TryUpdate(playerEntityId, candidate, static (ref MovementComponent movement, Vector3Int target) =>
        {
            movement.NextMapPosition = target;
        });
    }
}
