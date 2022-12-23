namespace Obsidian.API.Utilities;
public static class Extensions
{
    public static List<string> GetStateValues(this string key, Dictionary<string, List<string>> valueStores)
    {
        var list = new List<string>();

        var span = key.AsSpan();

        var count = 0;
        foreach (var (_, values) in valueStores)
        {
            if (span.Length > valueStores.Count)
            {
                var index = int.Parse($"{span[count]}{span[count + 1]}");

                var value = values[index];

                list.Add(value);
                count++;
            }
            else
            {
                var index = int.Parse($"{span[count]}");

                var value = values[index];

                list.Add(value);
            }

            count++;
        }

        return list;
    }
}
