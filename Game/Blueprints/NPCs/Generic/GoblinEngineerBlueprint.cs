using Engine.ECS.Components;
using Game.Blueprints.Classes;
using Game.Blueprints.Races;
using Game.Modules.Core.Components;
using Game.Modules.Energy.Components;

namespace Game.Blueprints.NPCs.Generic;

/// <summary>
/// A goblin engineer NPC: the Goblin race composed with the Engineer class, plus an
/// additional energy bonus on top of Engineer's own. Goblin.Build and Engineer.Build both
/// set DisplayTextComponent for this entity; since every blueprint uses <c>Merge</c> rather
/// than <c>Add</c>, the chain composes cleanly instead of throwing on the second write --
/// CoreModule's DisplayTextComponent merge lambda concatenates each stage's Name/Description
/// rather than colliding.
/// </summary>
public sealed class GoblinEngineerBlueprint(Goblin goblin, Engineer engineer) : IBlueprint
{
    private const string DisplayName = "Goblin Engineer";
    private const string Description = "Engineers. The incels of the goblin world. They have a hard time finding a date, which makes them extra angry. If there are any females in you party, they will attack them first.";

    public void Build(ComponentManager componentManager, int entityId)
    {
        goblin.Build(componentManager, entityId);
        engineer.Build(componentManager, entityId);

        componentManager.Merge(entityId, new DisplayTextComponent(DisplayName, Description));

        componentManager.TryUpdate(entityId, static (ref EnergyComponent energyComponent) =>
        {
            energyComponent.MaximumEnergy = (short)(energyComponent.MaximumEnergy * 1.1m);
            energyComponent.EnergyRecharge = (short)(energyComponent.EnergyRecharge * 1.1m);
        });
    }
}
