namespace Engine.Utilities;

/// <summary>Marks a type whose every value a method returns must be held in a using, which the Analyzers project's CAD0001 enforces.</summary>
/// <remarks>
/// For a scope whose Dispose does the work that makes it correct, such as a component write scope's notification: a scope never
/// disposed leaves a write nobody hears of. A value may also be returned, since the caller's own call is checked in turn.
/// </remarks>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class MustBeDisposedByUsingAttribute : Attribute;
