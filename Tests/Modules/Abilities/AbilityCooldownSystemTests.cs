using Engine.ECS.Components;
using Game.Modules.Abilities.Components;
using Game.Modules.Abilities.Systems;

namespace Tests.Modules.Abilities;

[TestClass]
public sealed class AbilityCooldownSystemTests
{
    private const int EntityId = 1;
    private static readonly Guid FirstAbilityId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondAbilityId = new("22222222-2222-2222-2222-222222222222");

    private static (AbilityCooldownSystem System, ComponentManager ComponentManager) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<AbilityInstanceComponent>();

        var system = new AbilityCooldownSystem(componentManager.GetMultiPool<AbilityInstanceComponent>());

        return (system, componentManager);
    }

    private static short CooldownOf(ComponentManager componentManager, int entityId, Guid abilityId)
    {
        var instances = componentManager.GetMultiPool<AbilityInstanceComponent>();
        for (var i = instances.GetFirstDenseIndex(entityId); i != -1; i = instances.GetNextDenseIndex(i))
        {
            var instance = instances.GetReadonlyByDenseIndex(i);
            if (instance.AbilityId == abilityId)
            {
                return instance.CooldownFramesRemaining;
            }
        }

        return -1;
    }

    [TestMethod]
    public void CooldownTicksDownByStripeCountPerVisit()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new AbilityInstanceComponent(FirstAbilityId, damageAmount: 0, cooldownFramesRemaining: 25));

        system.Update(default, (byte)(EntityId % 10));

        Assert.AreEqual(15, CooldownOf(componentManager, EntityId, FirstAbilityId));
    }

    [TestMethod]
    public void CooldownFlooredAtZero_NeverGoesNegative()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new AbilityInstanceComponent(FirstAbilityId, damageAmount: 0, cooldownFramesRemaining: 5));

        system.Update(default, (byte)(EntityId % 10));

        Assert.AreEqual(0, CooldownOf(componentManager, EntityId, FirstAbilityId));
    }

    [TestMethod]
    public void MultipleAbilityInstancesOnSameEntity_EachTickedIndependently()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new AbilityInstanceComponent(FirstAbilityId, damageAmount: 0, cooldownFramesRemaining: 25));
        componentManager.Merge(EntityId, new AbilityInstanceComponent(SecondAbilityId, damageAmount: 0, cooldownFramesRemaining: 0));

        system.Update(default, (byte)(EntityId % 10));

        Assert.AreEqual(15, CooldownOf(componentManager, EntityId, FirstAbilityId));
        Assert.AreEqual(0, CooldownOf(componentManager, EntityId, SecondAbilityId));
    }

    [TestMethod]
    public void EntityNotInThisStripe_IsNotTickedThisCall()
    {
        var (system, componentManager) = Build();
        componentManager.Merge(EntityId, new AbilityInstanceComponent(FirstAbilityId, damageAmount: 0, cooldownFramesRemaining: 25));

        var otherStripe = (byte)((EntityId % 10) + 1);
        system.Update(default, otherStripe);

        Assert.AreEqual(25, CooldownOf(componentManager, EntityId, FirstAbilityId));
    }
}
