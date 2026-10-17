namespace Game.Blueprints;

public static class DisplayNameCache
{
    public static string[] BuildDisplayNames(IReadOnlyList<string> personalNames, string raceName)
    {
        var displayNames = new string[personalNames.Count];
        for (var i = 0; i < displayNames.Length; i++)
        {
            displayNames[i] = $"{personalNames[i]} : {raceName}";
        }

        return displayNames;
    }
}
