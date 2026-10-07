using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using System.Globalization;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    internal void ReadGameRules(NbtCompound tag)
    {
        if (!tag.TryGetTag<NbtCompound>("game_rules", out var rules) && !tag.TryGetTag<NbtCompound>("GameRules", out rules)) return;
        foreach (var (name, child) in rules)
        {
            var value = child switch
            {
                NbtTag<string> text => text.Value,
                NbtTag<bool> flag => flag.Value ? "true" : "false",
                NbtTag<byte> flag => flag.Value != 0 ? "true" : "false",
                NbtTag<int> count => count.Value.ToString(CultureInfo.InvariantCulture),
                _ => null
            };
            if (value != null) LevelData.GameRules[name] = value;
        }
    }

    internal void WriteGameRules(INbtWriter writer)
    {
        writer.WriteCompoundStart("game_rules");
        foreach (var (name, value) in LevelData.GameRules)
        {
            if (bool.TryParse(value, out var enabled)) writer.WriteBool(name, enabled);
            else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) writer.WriteInt(name, count);
            else writer.WriteString(name, value);
        }
        writer.EndCompound();
    }
}
