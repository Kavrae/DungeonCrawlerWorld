namespace Engine.Math;

/// <summary>
/// Hands out unique integers within [minValue, maxValue] via rejection sampling over
/// MathUtility.Next -- reusable anywhere a caller needs random-but-collision-free identifiers
/// (e.g. CrawlerComponent's CrawlerNumber) without coordinating through EntityId.
/// </summary>
public sealed class UniqueNumberAllocator
{
    private readonly MathUtility _mathUtility;
    private readonly int _minValue;
    private readonly int _maxValueExclusiveUpperBound;

    private readonly HashSet<int> _allocatedNumbers = [];

    public UniqueNumberAllocator(MathUtility mathUtility, int minValue, int maxValue)
    {
        if (minValue > maxValue)
        {
            throw new ArgumentException($"minValue ({minValue}) is greater than maxValue ({maxValue}).");
        }

        _mathUtility = mathUtility;
        _minValue = minValue;
        _maxValueExclusiveUpperBound = maxValue + 1;
    }

    public int Allocate()
    {
        int candidate;
        do
        {
            candidate = _mathUtility.Next(_minValue, _maxValueExclusiveUpperBound);
        } while (!_allocatedNumbers.Add(candidate));

        return candidate;
    }
}
