namespace Engine.Diagnostics;

/// <summary>Who is on the other end of a diagnostics link: the game's first message.</summary>
/// <param name="Features">DiagnosticsFeatures as its name, the way reports write it.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record DiagnosticsLinkHello(int ProtocolVersion, int ProcessId, int? RandomSeed, string Features);
