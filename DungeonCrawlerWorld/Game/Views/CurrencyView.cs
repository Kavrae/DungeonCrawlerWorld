using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Currency.Components;

namespace Game.Views;

/// <summary>How much currency an entity holds.</summary>
public sealed class CurrencyView(ComponentManager componentManager)
{
    private readonly PackedComponentPool<CurrencyComponent> _currencies = componentManager.GetPackedPool<CurrencyComponent>();

    public bool TryGetCurrency(int entityId, out CurrencyComponent currency) => _currencies.TryGetReadonly(entityId, out currency);
}
