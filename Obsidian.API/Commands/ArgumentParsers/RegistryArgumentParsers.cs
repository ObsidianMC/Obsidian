using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.Registries;

namespace Obsidian.API.Commands.ArgumentParsers;

/// <summary>
/// Reads an item like vanilla's <c>item_stack</c> arguments, which the client completes from its item registry.
/// </summary>
/// <remarks>Components after the identifier, like <c>[damage=5]</c>, are accepted but ignored.</remarks>
[ArgumentParser("minecraft:item_stack")]
public sealed partial class ItemArgumentParser : BaseArgumentParser<Item>
{
    public override bool TryParseArgument(string input, CommandContext ctx, out Item result)
    {
        result = null!;

        var end = input.IndexOfAny(['[', '{']);
        if (!ResourceLocation.TryParse(end < 0 ? input : input[..end], out var identifier))
            return false;

        if (!ItemsRegistry.TryGet(identifier.Value.ToString(), out var item) || item.Id == ItemsRegistry.Air.Id)
            return false;

        result = item;
        return true;
    }
}

/// <summary>
/// Reads a block state like vanilla's <c>block_state</c> arguments, <c>minecraft:oak_stairs[facing=east]</c>, which
/// the client completes from its block registry. Whether the block exists is checked where it's placed.
/// </summary>
/// <remarks>Block entity data after the properties, like <c>{Items:[]}</c>, is accepted but ignored.</remarks>
[ArgumentParser("minecraft:block_state")]
public sealed partial class BlockStateArgumentParser : BaseArgumentParser<BlockStateArgument>
{
    public override bool TryParseArgument(string input, CommandContext ctx, out BlockStateArgument result)
    {
        result = null!;

        var nbt = input.IndexOf('{');
        if (nbt >= 0)
            input = input[..nbt];

        var open = input.IndexOf('[');
        if (!ResourceLocation.TryParse(open < 0 ? input : input[..open], out var identifier))
            return false;

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (open >= 0)
        {
            if (!input.EndsWith(']'))
                return false;

            foreach (var pair in input[(open + 1)..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = pair.IndexOf('=');
                if (separator <= 0)
                    return false;

                properties[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
            }
        }

        result = new BlockStateArgument(identifier.Value.ToString(), properties);
        return true;
    }
}

/// <summary>
/// Reads an identifier like vanilla's <c>resource_location</c> arguments.
/// </summary>
[ArgumentParser("minecraft:resource_location")]
public sealed partial class ResourceLocationArgumentParser : BaseArgumentParser<ResourceLocation>
{
    public override bool TryParseArgument(string input, CommandContext ctx, out ResourceLocation result)
    {
        var parsed = ResourceLocation.TryParse(input, out var location);
        result = location ?? default;
        return parsed;
    }
}

/// <summary>
/// Reads an entity type by its identifier, like <c>minecraft:zombie</c>, which the client completes from its entity
/// type registry.
/// </summary>
[ArgumentParser("minecraft:resource")]
public sealed partial class EntityTypeArgumentParser : BaseArgumentParser<EntityType>
{
    public override bool TryParseArgument(string input, CommandContext ctx, out EntityType result) =>
        RegistryNames.TryParseEnum(input, out result);

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteString("minecraft:entity_type");
    }
}

/// <summary>
/// Reads a mob effect by its identifier, like <c>minecraft:speed</c>, which the client completes from its mob effect
/// registry.
/// </summary>
[ArgumentParser("minecraft:resource")]
public sealed partial class PotionEffectArgumentParser : BaseArgumentParser<PotionEffect>
{
    public override bool TryParseArgument(string input, CommandContext ctx, out PotionEffect result) =>
        RegistryNames.TryParseEnum(input, out result);

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteString("minecraft:mob_effect");
    }
}

/// <summary>
/// Reads an enchantment by its identifier, like <c>minecraft:sharpness</c>, which the client completes from the
/// enchantments the server sent it.
/// </summary>
[ArgumentParser("minecraft:resource")]
public sealed partial class EnchantmentArgumentParser : BaseArgumentParser<EnchantmentDefinition>
{
    public override bool TryParseArgument(string input, CommandContext ctx, out EnchantmentDefinition result)
    {
        result = null!;

        if (!ResourceLocation.TryParse(input, out var identifier))
            return false;

        var name = identifier.Value.ToString();
        var enchantment = EnchantmentsRegistry.All.FirstOrDefault(x => x.Identifier == name);
        if (enchantment is null)
            return false;

        result = enchantment;
        return true;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteString("minecraft:enchantment");
    }
}

/// <summary>
/// Reads a particle type like vanilla's <c>particle</c> arguments, which the client completes from its particle type
/// registry.
/// </summary>
/// <remarks>Options after the identifier, like a dust particle's color, are accepted but ignored.</remarks>
[ArgumentParser("minecraft:particle")]
public sealed partial class ParticleTypeArgumentParser : BaseArgumentParser<ParticleType>
{
    public override bool TryParseArgument(string input, CommandContext ctx, out ParticleType result)
    {
        var end = input.IndexOfAny(['[', '{']);

        return RegistryNames.TryParseEnum(end < 0 ? input : input[..end], out result);
    }
}

/// <summary>Matches vanilla's registry identifiers with the members of Obsidian's enums.</summary>
internal static class RegistryNames
{
    /// <summary>
    /// Reads a vanilla identifier, like <c>minecraft:jump_boost</c> or <c>jump_boost</c>, as the enum member of the same
    /// name (<c>JumpBoost</c>).
    /// </summary>
    public static bool TryParseEnum<TEnum>(string input, out TEnum result) where TEnum : struct, Enum
    {
        result = default;

        if (!ResourceLocation.TryParse(input, out var identifier) || identifier.Value.Namespace != ResourceLocation.DefaultNamespace)
            return false;

        var name = identifier.Value.Path.Replace("_", string.Empty);
        foreach (var member in Enum.GetNames<TEnum>())
        {
            if (member.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                result = Enum.Parse<TEnum>(member);
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Reads vanilla's <c>score_holder</c> arguments: a target selector, a name, or <c>*</c> for every tracked holder.
/// Commands resolve it, since <c>*</c> depends on the scoreboard.
/// </summary>
[ArgumentParser("minecraft:score_holder")]
public sealed partial class ScoreHolderArgumentParser : BaseArgumentParser<ScoreHolderArgument>
{
    public override bool TryParseArgument(string input, CommandContext ctx, out ScoreHolderArgument result)
    {
        result = null!;

        if (input.Length == 0 || input.Length > 40)
            return false;

        if (input.StartsWith('@'))
        {
            if (!EntitySelector.TryParse(input, out var selector))
                return false;

            result = new ScoreHolderArgument(input, selector);
            return true;
        }

        result = new ScoreHolderArgument(input, null);
        return true;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        // Multiple holders are allowed.
        writer.WriteByte((sbyte)0x01);
    }
}
