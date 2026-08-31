namespace Engine.Utilities;

/// <summary>
/// True global, cross-layer mutable state -- reachable from Engine/Game/Presentation/exe alike
/// with no constructor wiring. Deliberately rare: the codebase otherwise favors DI (e.g.
/// UiInputController wraps TextInputEXT's own statics in instance fields specifically to stay
/// testable). Add a member here only when a piece of state genuinely needs to be readable from
/// every layer with no natural owning object to inject instead -- IsAdminModeOn is the first and,
/// as of writing, only case.
/// </summary>
public static class GlobalState
{
    /// <summary>F12-toggled, debug-build-only -- see UiInputController.HandleAdminModeToggle.</summary>
    public static bool IsAdminModeOn { get; set; }
}
