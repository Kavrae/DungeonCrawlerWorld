using Engine.Math;
using Game.Modules.Actions;
using Microsoft.Xna.Framework.Input;

namespace Presentation.UI;

/// <summary>Turns the player's WASD input into moves for PlayerCommands.</summary>
/// <remarks>
/// A freshly pressed movement key buffers a move in the direction of every movement key held this frame, so a tap
/// is kept after the key is released. Every frame, the held direction is also handed to the buffer's flush, which
/// uses it once the lock clears and nothing newer is buffered. Releasing keys cancels nothing.
///
/// claimedKeys is MapWindow's per-frame set of keys an earlier handler already used (today, Dodge's directional
/// confirm in ActionTargetingController.TryClaimDodgeDirectionalKey). A claimed key is neither a press nor held
/// for this frame, so any future handler can intercept a movement key without this class knowing why.
/// </remarks>
public sealed class PlayerMovementController(PlayerCommands playerCommands)
{
    private static readonly (Keys Key, Vector3Int Direction)[] MovementKeys =
    [
        (Keys.W, new Vector3Int(0, -1, 0)),
        (Keys.S, new Vector3Int(0, 1, 0)),
        (Keys.A, new Vector3Int(-1, 0, 0)),
        (Keys.D, new Vector3Int(1, 0, 0)),
    ];

    public void HandleInput(KeyboardState keyboardState, KeyboardState previousKeyboardState, IReadOnlySet<Keys> claimedKeys)
    {
        var heldDirection = new Vector3Int();
        var isFreshPress = false;
        foreach (var (key, direction) in MovementKeys)
        {
            if (!keyboardState.IsKeyDown(key) || claimedKeys.Contains(key))
            {
                continue;
            }

            heldDirection += direction;
            isFreshPress |= previousKeyboardState.IsKeyUp(key);
        }

        if (isFreshPress)
        {
            playerCommands.QueueMove(heldDirection);
        }

        playerCommands.Flush(heldDirection);
    }
}
