using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private StateProvider RandomBlockProvider(JsonElement json)
    {
        var states = this.BlockChoices(json.GetProperty("blocks")).ToArray();

        if (states.Length == 0)
        {
            return (_, _, _) => null;
        }

        return (_, random, _) => states[random.NextInt(states.Length)];
    }

    private IEnumerable<IBlock> BlockChoices(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in json.EnumerateArray())
            {
                foreach (var state in this.BlockChoices(entry))
                {
                    yield return state;
                }
            }

            yield break;
        }

        var id = json.GetString()!;

        if (id.StartsWith('#'))
        {
            foreach (var block in this.BlockTag(id[1..]))
            {
                yield return BlocksRegistry.GetDefault(block);
            }
        }
        else
        {
            yield return ReadBlock(json);
        }
    }
}
