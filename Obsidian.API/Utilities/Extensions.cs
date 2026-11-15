using Obsidian.API.Inventory;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Utilities;
/// <summary>
/// Provides conversions and helpers for vectors, item stacks, block state values and serialized data.
/// </summary>
public static partial class Extensions
{
    /// <summary>
    /// Creates a vector from the first three list entries in X, Y, Z order.
    /// </summary>
    public static Vector ToVector(this IList<int> value) =>
        new(value[0], value[1], value[2]);

    /// <summary>
    /// Returns the vector's components as a new list in X, Y, Z order.
    /// </summary>
    public static IList<int> ToList(this Vector value) =>
        [value.X, value.Y, value.Z];

    /// <summary>
    /// Tests whether the item stack is <c>null</c> or represents air.
    /// </summary>
    public static bool IsNullOrAir([NotNullWhen(false)] this ItemStack? item) => item == null || item.IsAir;

    /// <summary>
    /// Selects one value from each dictionary entry using the corresponding index in dictionary enumeration order.
    /// </summary>
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

    /// <summary>
    /// Returns the first row index whose sequence equals the supplied values, or -1 when none matches.
    /// </summary>
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

    /// <summary>
    /// Finds a state value, lowercasing Boolean text; throws <see cref="ArgumentException"/> if absent.
    /// </summary>
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
