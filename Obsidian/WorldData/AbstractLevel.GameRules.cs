using Obsidian.Nbt;
using System.Globalization;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    /// <summary>
    /// Reads the game rules in <paramref name="rules"/>, the payload of <c>game_rules.dat</c> (see
    /// <see cref="VanillaLevelData.SetGameRules"/> for writing them).
    /// </summary>
    internal void ReadGameRules(NbtCompound rules)
    {
        foreach (var (name, child) in rules)
        {
            if (GameRuleValue(child) is string value)
                LevelData.GameRules[name] = value;
        }
    }

    /// <summary>
    /// A saved game rule's value as <see cref="LevelData.GameRules"/> keeps it, or <c>null</c> for a tag that isn't one.
    /// </summary>
    internal static string? GameRuleValue(INbtTag tag) => tag switch
    {
        NbtTag<string> text => text.Value,
        NbtTag<bool> flag => flag.Value ? "true" : "false",
        NbtTag<byte> flag => flag.Value != 0 ? "true" : "false",
        NbtTag<int> count => count.Value.ToString(CultureInfo.InvariantCulture),
        _ => null
    };
}
