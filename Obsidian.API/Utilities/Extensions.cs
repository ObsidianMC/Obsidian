using Obsidian.API.Inventory;
using System.Diagnostics;

namespace Obsidian.API.Utilities;
public static partial class Extensions
{
    public static Vector ToVector(this IList<int> value) =>
        new(value[0], value[1], value[2]);

    public static IList<int> ToList(this Vector value) =>
        [value.X, value.Y, value.Z];

    public static bool IsNullOrAir(this ItemStack? item) => item == null || item.IsAir;

    public static List<string> GetStateValues(this int[] indexes, Dictionary<string, string[]> valueStores)
    {
        var list = new List<string>();

        var count = 0;
        foreach (var (_, values) in valueStores)
        {
            var index = indexes[count++];

            var value = values[index];

            list.Add(value);
        }

        return list;
    }

    public static int GetIndexFromJaggedArray(this int[][] array, int[] value)
    {
        for (int i = 0; i < array.Length; i++)
        {
            var child = array[i];

            if (Enumerable.SequenceEqual(child, value))
                return i;
        }

        return -1;
    }

    public static int GetIndexFromArray(this string[] array, string value)
    {
        var propertyValue = bool.TryParse(value, out _) ? value.ToLower() : value;

        if (!array.Contains(propertyValue))
            throw new ArgumentException("Failed to find value from the supplied array.", nameof(value));

        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] == propertyValue)
                return i;
        }

        throw new UnreachableException();
    }
}
