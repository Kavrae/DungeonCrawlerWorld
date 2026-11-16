namespace UIEngine.UI.Filtering;

/// <summary>Which of a filter's choices are checked: a bit per choice, by the choice's index across every group in order.</summary>
/// <remarks>
/// Every change advances Version and raises Changed once, however many choices it touched; a call that changes nothing
/// does neither. The checked ids go out (CheckedIds) and come back (SetCheckedIds) for a host to keep wherever it keeps
/// its settings; an id coming back that no choice has any more is dropped, never kept as a filter nothing shows.
/// </remarks>
public sealed class ListFilterSelection
{
    private const int BitsPerWord = 64;

    private readonly string[] _choiceIds;
    private readonly Dictionary<string, int> _choiceIndexById;
    private readonly ulong[] _checkedWords;

    public ListFilterSelection(IReadOnlyList<string> choiceIds)
    {
        _choiceIds = [.. choiceIds];
        _choiceIndexById = new Dictionary<string, int>(_choiceIds.Length, StringComparer.Ordinal);
        for (var index = 0; index < _choiceIds.Length; index++)
        {
            if (!_choiceIndexById.TryAdd(_choiceIds[index], index))
            {
                throw new ArgumentException($"Two filter choices share the id '{_choiceIds[index]}'.", nameof(choiceIds));
            }
        }

        _checkedWords = new ulong[(_choiceIds.Length + BitsPerWord - 1) / BitsPerWord];
    }

    public int ChoiceCount => _choiceIds.Length;

    public IReadOnlyList<string> ChoiceIds => _choiceIds;

    public int CheckedCount { get; private set; }

    public bool IsEmpty => CheckedCount == 0;

    /// <summary>Advances with every change to what is checked.</summary>
    public int Version { get; private set; }

    /// <summary>Raised once after each change to what is checked.</summary>
    public event Action<ListFilterSelection>? Changed;

    public bool IsChecked(int choiceIndex) => (_checkedWords[choiceIndex / BitsPerWord] & BitOf(choiceIndex)) != 0;

    /// <summary>The index of the choice whose id is choiceId; -1 for an id no choice has.</summary>
    public int IndexOf(string choiceId) => _choiceIndexById.TryGetValue(choiceId, out var index) ? index : -1;

    public void SetChecked(int choiceIndex, bool isChecked)
    {
        if (!WriteChecked(choiceIndex, isChecked))
        {
            return;
        }

        RaiseChanged();
    }

    public void Flip(int choiceIndex) => SetChecked(choiceIndex, !IsChecked(choiceIndex));

    public void Clear()
    {
        if (CheckedCount == 0)
        {
            return;
        }

        Array.Clear(_checkedWords);
        CheckedCount = 0;
        RaiseChanged();
    }

    /// <summary>The ids of the checked choices, in choice order.</summary>
    public IReadOnlyList<string> CheckedIds()
    {
        var checkedIds = new List<string>(CheckedCount);
        for (var index = 0; index < _choiceIds.Length; index++)
        {
            if (IsChecked(index))
            {
                checkedIds.Add(_choiceIds[index]);
            }
        }

        return checkedIds;
    }

    /// <summary>Checks exactly the choices named by checkedIds, leaving out any id no choice has.</summary>
    public void SetCheckedIds(IEnumerable<string> checkedIds)
    {
        var named = new HashSet<int>();
        foreach (var choiceId in checkedIds)
        {
            if (_choiceIndexById.TryGetValue(choiceId, out var index))
            {
                named.Add(index);
            }
        }

        var isChanged = false;
        for (var index = 0; index < _choiceIds.Length; index++)
        {
            isChanged |= WriteChecked(index, named.Contains(index));
        }

        if (isChanged)
        {
            RaiseChanged();
        }
    }

    private bool WriteChecked(int choiceIndex, bool isChecked)
    {
        if (IsChecked(choiceIndex) == isChecked)
        {
            return false;
        }

        _checkedWords[choiceIndex / BitsPerWord] ^= BitOf(choiceIndex);
        CheckedCount += isChecked ? 1 : -1;
        return true;
    }

    private void RaiseChanged()
    {
        Version++;
        Changed?.Invoke(this);
    }

    private static ulong BitOf(int choiceIndex) => 1UL << (choiceIndex % BitsPerWord);
}
