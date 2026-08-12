namespace Engine.Math;

/// <summary>
/// Defines the targetting parameters for an action.
/// </summary>
/// <remarks
/// This spec is shared between different modules, such as abilities and consumable effects,
/// as it does not contain any module-specific knowledge. 
/// 
/// The `Range` parameter specifies how far from the caster the shape's anchor tile may be placed.
/// The `AreaSize` parameter defines the footprint radius at that anchor. 
/// 
/// The use of these parameters is defined int he TargetShape parameter.
/// </remarks>
/// </summary>
public sealed record TargetingSpec(TargetShape Shape, int Range, int AreaSize = 0);
