namespace Engine.Diagnostics;

/// <summary>A game's split of its living entities into the ones every pool can hold and the smaller population the rest of the pools are held by.</summary>
/// <remarks>Engine knows neither population; the game supplies them, the way it supplies SystemManager.SimulatedTierCount. Diagnostics reads it to compare a pool against the population that can actually hold it.</remarks>
/// <param name="PartialPopulationName">What the partial population is called in a report (e.g. "Built").</param>
/// <param name="CountPartialPopulation">How many living entities belong to the partial population right now.</param>
/// <param name="IsHeldByEveryEntity">Whether a component type can be held by any living entity, rather than only by the partial population.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record EntityPopulationPolicy(string PartialPopulationName, Func<int> CountPartialPopulation, Func<Type, bool> IsHeldByEveryEntity);
