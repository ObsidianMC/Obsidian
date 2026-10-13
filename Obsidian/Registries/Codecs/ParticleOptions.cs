using Obsidian.Nbt;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// Reads the options of vanilla 1.21.11's particle types that take them (the ParticleOptions codecs of
/// ParticleTypes), for environment attributes' particles.
/// </summary>
/// <remarks>
/// Clients decode the options from registry data with vanilla's codecs, so they're checked like those codecs check
/// them and written as those codecs write what they read: colours as ints, defaults vanilla leaves out left out.
/// Blocks and items are vanilla's, as clients don't know plugins' ones, and items can't have components here.
/// </remarks>
internal static class ParticleOptions
{
    /// <summary>
    /// Reads a particle's options from its JSON into <paramref name="particle"/>.
    /// </summary>
    /// <param name="type">A particle type that takes options (see <see cref="RegistryIds.HasOptions"/>).</param>
    public static void Read(string type, JsonElement value, string path, NbtCompound particle)
    {
        switch (type)
        {
            case "minecraft:block":
            case "minecraft:block_marker":
            case "minecraft:block_crumble":
            case "minecraft:dust_pillar":
            case "minecraft:falling_dust":
                particle.Add(BlockState(Required(value, path, "block_state"), Field(path, "block_state")));
                break;
            case "minecraft:dust":
                particle.Add(new NbtTag<int>("color", Color(value, path, "color")));
                particle.Add(Scale(value, path));
                break;
            case "minecraft:dust_color_transition":
                particle.Add(new NbtTag<int>("from_color", Color(value, path, "from_color")));
                particle.Add(new NbtTag<int>("to_color", Color(value, path, "to_color")));
                particle.Add(Scale(value, path));
                break;
            case "minecraft:entity_effect":
            case "minecraft:flash":
            case "minecraft:tinted_leaves":
                var argb = ArgbIntColor(Required(value, path, "color"), Field(path, "color"));
                particle.Add(new NbtTag<int>("color", argb));
                break;
            case "minecraft:effect":
            case "minecraft:instant_effect":
                // An optional colour, -1 by default, and power, written only when they aren't their defaults.
                if (TryGet(value, "color", out var color)
                    && RgbIntColor(color, Field(path, "color")) is var rgb and not -1)
                {
                    particle.Add(new NbtTag<int>("color", rgb));
                }

                AddPower(value, path, particle, keepExplicitDefault: false);
                break;
            case "minecraft:dragon_breath":
                AddPower(value, path, particle, keepExplicitDefault: true);
                break;
            case "minecraft:sculk_charge":
                particle.Add(new NbtTag<float>("roll", Float(Required(value, path, "roll"), Field(path, "roll"))));
                break;
            case "minecraft:shriek":
                particle.Add(new NbtTag<int>("delay", Int(Required(value, path, "delay"), Field(path, "delay"))));
                break;
            case "minecraft:trail":
                particle.Add(Vector(Required(value, path, "target"), Field(path, "target")));
                particle.Add(new NbtTag<int>("color", Color(value, path, "color")));
                particle.Add(new NbtTag<int>(
                    "duration",
                    Int(Required(value, path, "duration"), Field(path, "duration"), 1, int.MaxValue)));
                break;
            case "minecraft:vibration":
                particle.Add(Destination(Required(value, path, "destination"), Field(path, "destination")));
                particle.Add(new NbtTag<int>(
                    "arrival_in_ticks",
                    Int(Required(value, path, "arrival_in_ticks"), Field(path, "arrival_in_ticks"))));
                break;
            case "minecraft:item":
                particle.Add(Item(Required(value, path, "item"), Field(path, "item")));
                break;
            default:
                throw Error(Field(path, "type"), $"{type}'s options can't be read");
        }
    }

    private static int Color(JsonElement value, string path, string field) =>
        RgbIntColor(Required(value, path, field), Field(path, field));

    // ScalableParticleOptionsBase.SCALE: a float from 0.01 to 4.
    private static NbtTag<float> Scale(JsonElement value, string path) =>
        new("scale", Float(Required(value, path, "scale"), Field(path, "scale"), 0.01f, 4f));

    // An optional power, 1 by default. Vanilla's optional field leaves out a value equal to its default: for effect
    // particles, a power of 1, but for dragon breath, whose default is an options object compared by reference, only
    // an omitted power, so an explicit 1 is kept there.
    private static void AddPower(JsonElement value, string path, NbtCompound particle, bool keepExplicitDefault)
    {
        if (!TryGet(value, "power", out var power))
            return;

        var read = Float(power, Field(path, "power"));
        if (keepExplicitDefault || read != 1f)
            particle.Add(new NbtTag<float>("power", read));
    }

    // BlockParticleOption's state: a block's name, or {"Name": ..., "Properties": {...}}. Like vanilla's, properties
    // the block doesn't have, or invalid values, give its default state when clients decode it.
    private static NbtCompound BlockState(JsonElement value, string path)
    {
        var state = new NbtCompound("block_state");

        if (value.ValueKind == JsonValueKind.String)
        {
            state.Add(new NbtTag<string>("Name", VanillaBlock(value, path)));
            return state;
        }

        Object(value, path);
        state.Add(new NbtTag<string>("Name", VanillaBlock(Required(value, path, "Name"), Field(path, "Name"))));

        if (!TryGet(value, "Properties", out var properties))
            return state;

        var propertiesPath = Field(path, "Properties");
        var tag = new NbtCompound("Properties");

        foreach (var property in Object(properties, propertiesPath).EnumerateObject())
            tag.Add(new NbtTag<string>(property.Name, String(property.Value, Field(propertiesPath, property.Name))));

        state.Add(tag);

        return state;
    }

    private static string VanillaBlock(JsonElement value, string path)
    {
        var name = Identifier(value, path);

        return name.StartsWith("minecraft:", StringComparison.Ordinal) && BlocksRegistry.TryGetDefault(name, out _)
            ? name
            : throw Error(path, $"{name} isn't one of vanilla's blocks");
    }

    // ItemParticleOption's item: an item's name or {"id": ...}, written as {"id": ...}. It can't be air, which
    // vanilla's particle refuses as an empty stack.
    private static NbtCompound Item(JsonElement value, string path)
    {
        var idPath = path;
        var id = value;

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (TryGet(value, "components", out _))
                throw Error(Field(path, "components"), "particles' items can't have components here");

            idPath = Field(path, "id");
            id = Required(value, path, "id");
        }

        var name = Identifier(id, idPath);
        var vanilla = name.StartsWith("minecraft:", StringComparison.Ordinal)
            && ItemsRegistry.TryGet(name, out var item)
            && item.Id != ItemsRegistry.Air.Id;

        if (!vanilla)
            throw Error(idPath, $"{name} isn't one of vanilla's items, other than air");

        return new NbtCompound("item")
        {
            new NbtTag<string>("id", name)
        };
    }

    // Vec3.CODEC: a list of 3 doubles.
    private static NbtList Vector(JsonElement value, string path)
    {
        if (Array(value, path).GetArrayLength() != 3)
            throw Error(path, $"expected a list of 3 numbers, not {value.GetArrayLength()}");

        var list = new NbtList(NbtTagType.Double, "target");
        for (var i = 0; i < 3; i++)
            list.Add(new NbtTag<double>("", Double(value[i], Index(path, i))));

        return list;
    }

    // VibrationParticleOption's destination: a block position source, {"type": "minecraft:block", "pos": [x, y, z]}.
    // Vanilla refuses entity position sources here.
    private static NbtCompound Destination(JsonElement value, string path)
    {
        Object(value, path);

        var typePath = Field(path, "type");
        var type = Identifier(Required(value, path, "type"), typePath);

        if (type != "minecraft:block")
        {
            throw Error(typePath, type == "minecraft:entity"
                ? "entity position sources aren't allowed"
                : $"{type} isn't a position source type");
        }

        var posPath = Field(path, "pos");
        var pos = Required(value, path, "pos");
        if (Array(pos, posPath).GetArrayLength() != 3)
            throw Error(posPath, $"expected a list of 3 ints, not {pos.GetArrayLength()}");

        int[] position = [.. pos.EnumerateArray().Select((coordinate, i) => Int(coordinate, Index(posPath, i)))];

        return new NbtCompound("destination")
        {
            new NbtTag<string>("type", type),
            new NbtArray<int>("pos", position)
        };
    }
}
