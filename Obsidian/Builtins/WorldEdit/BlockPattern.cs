using System.Globalization;
using Obsidian.API;
using Obsidian.API.World.Generator;

namespace Obsidian.Builtins.WorldEdit;

internal sealed class BlockPattern
{
    private readonly List<(IBlock Block, double Weight)> blocks = [];
    private readonly double total;

    internal BlockPattern(string input)
    {
        foreach (var token in Split(input))
        {
            var percent = token.IndexOf('%');
            var weight = 1.0;
            if (percent >= 0 && (!double.TryParse(token[..percent], NumberStyles.Float, CultureInfo.InvariantCulture, out weight)
                || !double.IsFinite(weight) || weight <= 0 || weight > 100))
                throw new ArgumentException("Pattern weights must be greater than zero and at most 100 (e.g. 80%stone,20%dirt).");
            this.blocks.Add((Resolve(percent < 0 ? token : token[(percent + 1)..]), weight));
            this.total += weight;
        }
    }

    internal IBlock Pick()
    {
        var pick = Random.Shared.NextDouble() * this.total;
        foreach (var (block, weight) in this.blocks)
        {
            pick -= weight;
            if (pick < 0)
                return block;
        }
        return this.blocks[^1].Block;
    }

    internal static int StateId(IBlock block) => block.State?.Id ?? block.DefaultId;

    internal static IBlock Resolve(string input)
    {
        var open = input.IndexOf('[');
        var name = open < 0 ? input : input[..open];
        if (name.Length == 0 || input.Contains('{') || input.Contains('}'))
            throw new ArgumentException("Use a block identifier with optional [property=value] states; NBT literals are unsupported.");
        if (!name.Contains(':'))
            name = "minecraft:" + name;
        var properties = new Dictionary<string, string>();
        if (open >= 0)
        {
            if (!input.EndsWith(']'))
                throw new ArgumentException("Block states must end with ].");
            foreach (var pair in input[(open + 1)..^1].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=');
                if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace) || !properties.TryAdd(parts[0], parts[1]))
                    throw new ArgumentException("Use block[property=value] with no repeated properties.");
            }
        }
        IBlock block;
        try { block = new SimpleBlockState { Name = name, Properties = properties }.ToBlock(); }
        catch (InvalidOperationException ex) { throw new ArgumentException(ex.Message, ex); }
        var actual = block.ToSimpleState();
        if (properties.Any(pair => !actual.Properties.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new ArgumentException($"Invalid block states for {name}.");
        return block;
    }

    internal static IEnumerable<string> Split(string input)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < input.Length; index++)
        {
            if (input[index] == '[') depth++;
            if (input[index] == ']') depth--;
            if (depth is < 0 or > 1)
                throw new ArgumentException("Invalid block pattern brackets.");
            if (input[index] == ',' && depth == 0)
            {
                if (index == start)
                    throw new ArgumentException("Block patterns cannot contain empty entries.");
                yield return input[start..index];
                start = index + 1;
            }
        }
        if (depth != 0 || start == input.Length)
            throw new ArgumentException("A block pattern is required, with balanced brackets and no empty entries.");
        yield return input[start..];
    }
}

internal sealed class BlockMask(string input)
{
    private readonly (IBlock Block, bool AnyState)[] blocks = input == "*" ? []
        : BlockPattern.Split(input).Select(token => (BlockPattern.Resolve(token), !token.Contains('['))).ToArray();

    internal bool Matches(IBlock block) => input == "*"
        || this.blocks.Any(entry => entry.AnyState ? entry.Block.RegistryId == block.RegistryId : BlockPattern.StateId(entry.Block) == BlockPattern.StateId(block));
}
