namespace Game.Modules.Currency;

/// <summary>Which balance on a CurrencyComponent an action/drag/context-menu operation targets -- an explicit enum rather than an "isGold" bool so a future third currency is a new case here, not a bool hard-limited to two values threaded through every call site.</summary>
public enum CurrencyType
{
    Gold,
    Credits,
}
