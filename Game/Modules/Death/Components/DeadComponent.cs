namespace Game.Modules.Death.Components;

/// <summary> Marks an entity as a corpse</summary>
/// <remarks>KilledBy is EntityDiedEvent.Source: for an entity killer, its stable key and the name and
/// crawler number it had, which outlive the killer's unload. DiedAtFrame is the
/// EngineTime.FrameCount DeathSystem.Update was processing when this corpse's EntityDiedEvent was
/// dispatched -- a raw tick count until a real in-game calendar/clock exists (see TODO.md).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct DeadComponent(Game.World.ActionSource KilledBy, long DiedAtFrame)
{
    public override readonly string ToString() => $"KilledBy : {KilledBy}\nDiedAtFrame : {DiedAtFrame}";
}
