namespace Engine.Math;

/// <summary>Names one random stream of a session: the module that owns it and a name of its own.</summary>
/// <remarks>Owned by a module's Id, as a setting is, so a mod's streams never shift a built-in's and a module replacing another by Id draws from the stream of the one it replaced.</remarks>
/// <param name="Owner">The owning module's Id.</param>
/// <param name="Name">The stream's name within its owner: a constant, never built per use.</param>
public readonly record struct RandomStreamKey(Guid Owner, string Name);
