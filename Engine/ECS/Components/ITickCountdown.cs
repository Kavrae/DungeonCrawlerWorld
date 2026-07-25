namespace Engine.ECS.Components;

/// <summary>
/// Implemented by a component that represents "a countdown until the next periodic tick,"
/// present on an entity only while some ongoing timed effect is active (a burn, a poison
/// duration, a hazard-exposure window, an aura-exposure window). Lets
/// Engine.ECS.Systems.CountdownTicker.Tick decrement/fire these generically for any module
/// that has one, instead of each hand-rolling the identical "decrement while > 1, otherwise
/// fire" loop.
/// </summary>
public interface ITickCountdown
{
    int FramesUntilNextTick { get; set; }
}
