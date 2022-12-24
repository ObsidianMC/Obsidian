using System.Diagnostics;

namespace Obsidian.API.Utilities;
public static class Extensions
{
    public static List<string> GetStateValues(this string key, Dictionary<string, string[]> valueStores)
    {
        var list = new List<string>();
        var vals = key.Split("-");

        var count = 0;
        foreach (var (_, values) in valueStores)
        {
            var index = int.Parse(vals[count++]);

            var value = values[index];

            list.Add(value);
        }

        return list;
    }

    public static string GetIndexFromArray(this string[] array, string value)
    {
        var propertyValue = bool.TryParse(value, out _) ? value.ToLower() : value;

        if (!array.Contains(propertyValue))
            throw new ArgumentException("Failed to find value from the supplied array.", nameof(value));

        for(int i = 0; i < array.Length; i++)
        {
            if (array[i] == propertyValue)
                return $"{i}";
        }

        throw new UnreachableException();
    }
}
