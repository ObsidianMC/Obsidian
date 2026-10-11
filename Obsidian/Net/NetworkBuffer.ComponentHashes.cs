using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Utilities;
using Obsidian.API.Registries;
using System.IO;
using System.Text;
using H = Obsidian.API.Inventory.DataComponents.ComponentHash;

namespace Obsidian.Net;

public partial class NetworkBuffer
{
    /// <summary>
    /// Resolves a connection's registry ID to its resource location for component hashing. Supply this for a client
    /// connected to another server; dynamic registry IDs are not universal. Missing names produce an omitted hash.
    /// </summary>
    public Func<string, int, string?>? ComponentRegistryName { get; set; }

    /// <summary>Hashes a component with vanilla's persistent codec/HashOps format, not its network bytes.
    /// False means the value needs an unsupported codec or an unavailable registry entry.</summary>
    public bool TryHashDataComponent(DataComponent component, out int hash)
    {
        var buffer = new NetworkBuffer { ComponentRegistryName = this.ComponentRegistryName };
        buffer.WriteDataComponent(component);
        buffer.Reset();
        buffer.SkipComponent(component.Type);
        if (buffer.Offset != buffer.Size)
            throw new InvalidDataException("Trailing bytes in component value.");

        buffer.Reset();
        try
        {
            hash = buffer.HashComponent(component.Type);
            return buffer.Offset == buffer.Size;
        }
        catch (UnsupportedComponentHashException)
        {
            hash = 0;
            return false;
        }
    }

    /// <summary>Writes a click prediction. Unsupported hashes are omitted; vanilla then resynchronizes the slot.</summary>
    public void WriteHashedItemStack(ItemStack? stack)
    {
        if (stack is null || stack.IsAir || stack.Count <= 0)
        {
            this.WriteBoolean(false);
            return;
        }

        var hashes = new List<(DataComponentType Type, int Hash)>();
        foreach (var component in stack.Patch)
        {
            if (this.TryHashDataComponent(component, out var hash))
                hashes.Add((component.Type, hash));
        }

        this.WriteBoolean(true);
        this.WriteVarInt(stack.Holder.Id);
        this.WriteVarInt(stack.Count);
        this.WriteVarInt(hashes.Count);
        foreach (var (type, hash) in hashes)
        {
            this.WriteVarInt(type);
            this.WriteInt(hash);
        }

        this.WriteVarInt(stack.RemoveComponents.Count);
        foreach (var type in stack.RemoveComponents)
            this.WriteVarInt(type);
    }

    /// <summary>Writes already computed hashes, for relaying or capturing ContainerClick values.</summary>
    public void WriteHashedItemStack(IHashedItemStack? stack)
    {
        this.WriteBoolean(stack is not null);
        if (stack is null)
            return;

        this.WriteVarInt(stack.Holder.Id);
        this.WriteVarInt(stack.Count);
        this.WriteVarInt(stack.HashedComponents.Count);
        foreach (var (type, hash) in stack.HashedComponents)
        {
            this.WriteVarInt(type);
            this.WriteInt(hash);
        }

        this.WriteVarInt(stack.ComponentsToRemove.Count);
        foreach (var type in stack.ComponentsToRemove)
            this.WriteVarInt(type);
    }

    private sealed class ReceivedHashedStack(Item holder, int count, Func<string, int, string?>? registryNames) : IHashedItemStack
    {
        public Dictionary<DataComponentType, int> HashedComponents { get; } = [];
        public List<DataComponentType> ComponentsToRemove { get; } = [];
        public int Count { get; set; } = count;
        public Item Holder { get; } = holder;
        public Material Type => this.Holder.Type;

        public bool Compare(ItemStack other)
        {
            if (other is null || this.Count != other.Count || this.Type != other.Type
                || !this.ComponentsToRemove.ToHashSet().SetEquals(other.RemoveComponents))
                return false;

            var patch = other.Patch.ToArray();
            if (patch.Length != this.HashedComponents.Count)
                return false;

            var hasher = new NetworkBuffer { ComponentRegistryName = registryNames };
            return patch.All(component => this.HashedComponents.TryGetValue(component.Type, out var received)
                && hasher.TryHashDataComponent(component, out var expected) && received == expected);
        }
    }

    private sealed class UnsupportedComponentHashException : Exception;

    private string RegistryName(string registry, int id)
    {
        if (this.ComponentRegistryName is not null)
            return this.ComponentRegistryName(registry, id) ?? throw new UnsupportedComponentHashException();

        // Built-in registries have fixed IDs; dynamic registries must come from the connection.
        return registry switch
        {
            "minecraft:potion" when Enum.IsDefined((Potion)id) => "minecraft:" + ((Potion)id).ToString().ToSnakeCase(),
            "minecraft:mob_effect" when Enum.IsDefined((MobEffect)id) => "minecraft:" + ((MobEffect)id).ToString().ToSnakeCase(),
            "minecraft:item" => ItemsRegistry.Get(id).UnlocalizedName,
            "minecraft:enchantment" when id >= 0 && id < EnchantmentsRegistry.All.Count => EnchantmentsRegistry.All[id].Identifier,
            _ => throw new UnsupportedComponentHashException()
        };
    }

    private int RegistryHash(string registry) => H.String(this.RegistryName(registry, this.ReadVarInt()));

    private int[] HashList(Func<int> read)
    {
        var result = new int[this.ReadComponentCount()];
        for (var i = 0; i < result.Length; i++)
            result[i] = read();

        return result;
    }

    private static int Record(List<(string Key, int Value)> fields) => H.Record(fields.ToArray());

    private static readonly string[] RarityNames = ["common", "uncommon", "rare", "epic"];
    private static readonly string[] SwingNames = ["none", "whack", "stab"];
    private static readonly string[] ExplosionNames = ["small_ball", "large_ball", "star", "creeper", "burst"];
    private static readonly string[] ModifierOperations = ["add_value", "add_multiplied_base", "add_multiplied_total"];
    private static readonly string[] ModifierSlots =
        ["any", "mainhand", "offhand", "hand", "feet", "legs", "chest", "head", "armor", "body", "saddle"];

    private int HashIdSet(string registry)
    {
        var count = this.ReadComponentCount();
        if (count == 0)
            return H.String("#" + this.ReadString());

        var values = new int[count - 1];
        for (var i = 0; i < values.Length; i++)
            values[i] = this.RegistryHash(registry);

        // RegistryCodecs' compact list representation writes a singleton as the identifier itself.
        return values.Length == 1 ? values[0] : H.List(values);
    }

    private int HashComponent(DataComponentType type)
    {
        switch (type)
        {
            case DataComponentType.MaxStackSize: case DataComponentType.MaxDamage: case DataComponentType.Damage:
            case DataComponentType.RepairCost: case DataComponentType.MapId: case DataComponentType.OminousBottleAmplifier:
                return H.Int(this.ReadVarInt());
            case DataComponentType.DyedColor: case DataComponentType.MapColor: return H.Int(this.ReadInt());
            case DataComponentType.MinimumAttackCharge: case DataComponentType.PotionDurationScale: return H.Float(this.ReadSingle());
            case DataComponentType.Unbreakable: case DataComponentType.Glider: return H.Record();
            case DataComponentType.EnchantmentGlintOverride: return H.Boolean(this.ReadBoolean());
            case DataComponentType.ItemModel: case DataComponentType.TooltipStyle: case DataComponentType.NoteBlockSound:
                return H.String(this.ReadString());
            case DataComponentType.Rarity: return H.String(RarityNames[this.ReadVarInt()]);
            case DataComponentType.BaseColor: case DataComponentType.WolfCollar: case DataComponentType.CatCollar:
            case DataComponentType.SheepColor: case DataComponentType.ShulkerColor:
            case DataComponentType.TropicalFishBaseColor: case DataComponentType.TropicalFishPatternColor:
                return H.String(((Dye)this.ReadVarInt()).ToString().ToSnakeCase());
            case DataComponentType.Enchantable: return H.Record(("value", H.Int(this.ReadVarInt())));
            case DataComponentType.DamageResistant: return H.Record(("types", H.String("#" + this.ReadString())));
            case DataComponentType.ProvidesBannerPatterns: return H.String("#" + this.ReadString());
            case DataComponentType.CustomName: case DataComponentType.ItemName: return this.HashNbt(true);
            case DataComponentType.Lore: return H.List(this.HashList(() => this.HashNbt(true)));
            case DataComponentType.CustomData: case DataComponentType.BucketEntityData: case DataComponentType.IntangibleProjectile:
            case DataComponentType.DebugStickState: case DataComponentType.MapDecorations: case DataComponentType.Recipes:
            case DataComponentType.ContainerLoot:
                return this.HashNbt(false);
            case DataComponentType.Enchantments: case DataComponentType.StoredEnchantments:
                var entries = new List<(int, int)>();
                var count = this.ReadComponentCount();
                for (var i = 0; i < count; i++)
                    entries.Add((this.RegistryHash("minecraft:enchantment"), H.Int(this.ReadVarInt())));
                return H.Map(entries);
            case DataComponentType.AttributeModifiers:
                return H.List(this.HashList(() =>
                {
                    var modifier = new List<(string, int)>
                    {
                        ("type", this.RegistryHash("minecraft:attribute")),
                        ("id", H.String(this.ReadString())),
                        ("amount", H.Number(11, BitConverter.DoubleToInt64Bits(this.ReadDouble()), 8)),
                        ("operation", H.String(ModifierOperations[this.ReadVarInt()]))
                    };

                    var slot = this.ReadVarInt();
                    if (slot != 0)
                        modifier.Add(("slot", H.String(ModifierSlots[slot])));

                    var display = this.ReadVarInt();
                    if (display == 1)
                        modifier.Add(("display", H.Record(("type", H.String("hidden")))));
                    if (display == 2)
                        modifier.Add(("display", H.Record(("type", H.String("override")), ("value", this.HashNbt(true)))));

                    return Record(modifier);
                }));
            case DataComponentType.Tool:
                var rules = this.HashList(() =>
                {
                    var rule = new List<(string, int)> { ("blocks", this.HashIdSet("minecraft:block")) };
                    if (this.ReadBoolean())
                        rule.Add(("speed", H.Float(this.ReadSingle())));
                    if (this.ReadBoolean())
                        rule.Add(("correct_for_drops", H.Boolean(this.ReadBoolean())));

                    return Record(rule);
                });

                var tool = new List<(string, int)> { ("rules", H.List(rules)) };
                var miningSpeed = this.ReadSingle();
                if (miningSpeed != 1)
                    tool.Add(("default_mining_speed", H.Float(miningSpeed)));

                var blockDamage = this.ReadVarInt();
                if (blockDamage != 1)
                    tool.Add(("damage_per_block", H.Int(blockDamage)));

                if (!this.ReadBoolean())
                    tool.Add(("can_destroy_blocks_in_creative", H.Boolean(false)));

                return Record(tool);
            case DataComponentType.Repairable: return H.Record(("items", this.HashIdSet("minecraft:item")));
            case DataComponentType.UseEffects:
                var use = new List<(string, int)>();
                if (this.ReadBoolean())
                    use.Add(("can_sprint", H.Boolean(true)));
                if (!this.ReadBoolean())
                    use.Add(("interact_vibrations", H.Boolean(false)));

                var speed = this.ReadSingle();
                if (speed != 0.2f)
                    use.Add(("speed_multiplier", H.Float(speed)));

                return Record(use);
            case DataComponentType.CustomModelData:
                var model = new List<(string, int)>();
                var modelLists = new (string, Func<int>)[]
                {
                    ("floats", () => H.Float(this.ReadSingle())),
                    ("flags", () => H.Boolean(this.ReadBoolean())),
                    ("strings", () => H.String(this.ReadString())),
                    ("colors", () => H.Int(this.ReadInt()))
                };
                foreach (var (key, reader) in modelLists)
                {
                    var list = this.HashList(reader);
                    if (list.Length > 0)
                        model.Add((key, H.List(list)));
                }

                return Record(model);
            case DataComponentType.TooltipDisplay:
                var tooltip = new List<(string, int)>();
                if (this.ReadBoolean())
                    tooltip.Add(("hide_tooltip", H.Boolean(true)));

                var hidden = this.HashList(() => H.String(OpaqueDataComponent.GetIdentifier((DataComponentType)this.ReadVarInt())));
                if (hidden.Length > 0)
                    tooltip.Add(("hidden_components", H.List(hidden)));

                return Record(tooltip);
            case DataComponentType.Weapon:
                var weapon = new List<(string, int)>();
                var damage = this.ReadVarInt();
                var seconds = this.ReadSingle();
                if (damage != 1)
                    weapon.Add(("item_damage_per_attack", H.Int(damage)));
                if (seconds != 0)
                    weapon.Add(("disable_blocking_for_seconds", H.Float(seconds)));

                return Record(weapon);
            case DataComponentType.AttackRange:
                var range = new List<(string, int)>();
                var rangeDefaults = new (string, float)[]
                {
                    ("min_reach", 0), ("max_reach", 3), ("min_creative_reach", 0),
                    ("max_creative_reach", 5), ("hitbox_margin", 0.3f), ("mob_factor", 1)
                };
                foreach (var (key, normal) in rangeDefaults)
                {
                    var value = this.ReadSingle();
                    if (value != normal)
                        range.Add((key, H.Float(value)));
                }

                return Record(range);
            case DataComponentType.SwingAnimation:
                var swing = new List<(string, int)>();
                var animation = this.ReadVarInt();
                var ticks = this.ReadVarInt();
                if (animation != 1)
                    swing.Add(("type", H.String(SwingNames[animation])));
                if (ticks != 6)
                    swing.Add(("duration", H.Int(ticks)));

                return Record(swing);
            case DataComponentType.UseRemainder: return this.HashStack() ?? throw new UnsupportedComponentHashException();
            case DataComponentType.ChargedProjectiles: case DataComponentType.BundleContents:
                return H.List(this.HashList(() => this.HashStack() ?? throw new UnsupportedComponentHashException()));
            case DataComponentType.Container:
                var slots = new List<int>();
                var slotCount = this.ReadComponentCount();
                for (var i = 0; i < slotCount; i++)
                {
                    if (this.HashStack() is int item)
                        slots.Add(H.Record(("slot", H.Int(i)), ("item", item)));
                }

                return H.List(slots);
            case DataComponentType.PotionContents: return this.HashPotion();
            case DataComponentType.FireworkExplosion: return this.HashExplosion();
            case DataComponentType.Fireworks:
                var fireworks = new List<(string, int)>();
                var flight = this.ReadVarInt();
                if (flight != 0)
                    fireworks.Add(("flight_duration", H.Byte((byte)flight)));

                var explosions = this.HashList(this.HashExplosion);
                if (explosions.Length != 0)
                    fireworks.Add(("explosions", H.List(explosions)));

                return Record(fireworks);
            case DataComponentType.Food:
                var food = new List<(string, int)> { ("nutrition", H.Int(this.ReadVarInt())), ("saturation", H.Float(this.ReadSingle())) };
                if (this.ReadBoolean())
                    food.Add(("can_always_eat", H.Boolean(true)));

                return Record(food);
            case DataComponentType.UseCooldown:
                var cooldown = new List<(string, int)> { ("seconds", H.Float(this.ReadSingle())) };
                if (this.ReadBoolean())
                    cooldown.Add(("cooldown_group", H.String(this.ReadString())));

                return Record(cooldown);
            case DataComponentType.WritableBookContent:
                var pages = this.HashList(() => this.HashFilterable(() => H.String(this.ReadString())));
                return pages.Length == 0 ? H.Record() : H.Record(("pages", H.List(pages)));
            case DataComponentType.WrittenBookContent:
                var book = new List<(string, int)>
                {
                    ("title", this.HashFilterable(() => H.String(this.ReadString()))),
                    ("author", H.String(this.ReadString()))
                };

                var generation = this.ReadVarInt();
                if (generation != 0)
                    book.Add(("generation", H.Int(generation)));

                var writtenPages = this.HashList(() => this.HashFilterable(() => this.HashNbt(true)));
                if (writtenPages.Length != 0)
                    book.Add(("pages", H.List(writtenPages)));
                if (this.ReadBoolean())
                    book.Add(("resolved", H.Boolean(true)));

                return Record(book);
            case DataComponentType.SuspiciousStewEffects:
                return H.List(this.HashList(() =>
                {
                    var effect = this.RegistryHash("minecraft:mob_effect");
                    var duration = this.ReadVarInt();
                    return duration == 160 ? H.Record(("id", effect)) : H.Record(("id", effect), ("duration", H.Int(duration)));
                }));
            case DataComponentType.BlockState:
                var state = new List<(int, int)>();
                var properties = this.ReadComponentCount();
                for (var i = 0; i < properties; i++)
                    state.Add((H.String(this.ReadString()), H.String(this.ReadString())));

                return H.Map(state);
            case DataComponentType.BannerPatterns:
                return H.List(this.HashList(() => H.Record(("pattern", this.HashRegistryHolder("minecraft:banner_pattern")),
                    ("color", H.String(((Dye)this.ReadVarInt()).ToString().ToSnakeCase())))));
            case DataComponentType.Trim:
                return H.Record(
                    ("material", this.HashRegistryHolder("minecraft:trim_material")),
                    ("pattern", this.HashRegistryHolder("minecraft:trim_pattern")));
            case DataComponentType.PotDecorations: return H.List(this.HashList(() => this.RegistryHash("minecraft:item")));
            default: throw new UnsupportedComponentHashException();
        }
    }

    private int? HashStack()
    {
        var count = this.ReadVarInt();
        if (count <= 0)
            return null;

        var fields = new List<(string, int)> { ("id", this.RegistryHash("minecraft:item")), ("count", H.Int(count)) };
        var added = this.ReadComponentCount();
        var removed = this.ReadComponentCount();
        var patch = new List<(int, int)>();
        for (var i = 0; i < added; i++)
        {
            var type = (DataComponentType)this.ReadVarInt();
            patch.Add((H.String(OpaqueDataComponent.GetIdentifier(type)), this.HashComponent(type)));
        }

        for (var i = 0; i < removed; i++)
            patch.Add((H.String("!" + OpaqueDataComponent.GetIdentifier((DataComponentType)this.ReadVarInt())), H.Record()));

        if (patch.Count > 0)
            fields.Add(("components", H.Map(patch)));

        return Record(fields);
    }

    private int HashRegistryHolder(string registry)
    {
        var id = this.ReadVarInt();
        if (id == 0)
            throw new UnsupportedComponentHashException();

        return H.String(this.RegistryName(registry, id - 1));
    }

    private int HashFilterable(Func<int> read)
    {
        var raw = read();
        return this.ReadBoolean() ? H.Record(("raw", raw), ("filtered", read())) : H.Record(("raw", raw));
    }

    private int HashPotion()
    {
        var fields = new List<(string, int)>();
        if (this.ReadBoolean())
            fields.Add(("potion", this.RegistryHash("minecraft:potion")));
        if (this.ReadBoolean())
            fields.Add(("custom_color", H.Int(this.ReadInt())));

        var effects = this.HashList(() =>
        {
            var id = this.RegistryHash("minecraft:mob_effect");
            var details = this.HashEffectDetails();
            details.Add(("id", id));
            return Record(details);
        });
        if (effects.Length != 0)
            fields.Add(("custom_effects", H.List(effects)));
        if (this.ReadBoolean())
            fields.Add(("custom_name", H.String(this.ReadString())));

        return Record(fields);
    }

    private List<(string, int)> HashEffectDetails()
    {
        var fields = new List<(string, int)>();
        var amplifier = this.ReadVarInt();
        if (amplifier != 0)
            fields.Add(("amplifier", H.Byte((byte)amplifier)));

        var duration = this.ReadVarInt();
        if (duration != 0)
            fields.Add(("duration", H.Int(duration)));

        if (this.ReadBoolean())
            fields.Add(("ambient", H.Boolean(true)));
        if (!this.ReadBoolean())
            fields.Add(("show_particles", H.Boolean(false)));

        fields.Add(("show_icon", H.Boolean(this.ReadBoolean())));
        if (this.ReadBoolean())
            fields.Add(("hidden_effect", Record(this.HashEffectDetails())));

        return fields;
    }

    private int HashExplosion()
    {
        var fields = new List<(string, int)> { ("shape", H.String(ExplosionNames[this.ReadVarInt()])) };
        var colors = this.HashList(() => H.Int(this.ReadInt()));
        if (colors.Length != 0)
            fields.Add(("colors", H.List(colors)));

        var fades = this.HashList(() => H.Int(this.ReadInt()));
        if (fades.Length != 0)
            fields.Add(("fade_colors", H.List(fades)));

        if (this.ReadBoolean())
            fields.Add(("has_trail", H.Boolean(true)));
        if (this.ReadBoolean())
            fields.Add(("has_twinkle", H.Boolean(true)));

        return Record(fields);
    }

    private int HashNbt(bool chat) => this.HashNbt(this.ReadByte(), chat);

    private int HashNbt(int tag, bool chat)
    {
        switch (tag)
        {
            case 0: return H.Bytes([1]);
            case 1: return H.Byte(this.ReadByte());
            case 2: return H.Number(7, this.ReadShort(), 2);
            case 3: return H.Int(this.ReadInt());
            case 4: return H.Number(9, this.ReadLong(), 8);
            case 5: return H.Float(this.ReadSingle());
            case 6: return H.Number(11, BitConverter.DoubleToInt64Bits(this.ReadDouble()), 8);
            case 8: return H.String(this.ReadNbtString());
            case 9:
                var element = this.ReadByte();
                var length = this.ReadInt();
                if (chat && element is not (8 or 9 or 10) && length != 0)
                    throw new UnsupportedComponentHashException();

                var values = new int[length];
                for (var i = 0; i < length; i++)
                    values[i] = this.HashNbt(element, chat);

                return H.List(values);
            case 10:
                var fields = new List<(string Key, int Value)>();
                int next;
                while ((next = this.ReadByte()) != 0)
                {
                    var name = this.ReadNbtString();
                    var boolean = chat && name is "bold" or "italic" or "underlined" or "strikethrough" or "obfuscated" or "interpret";
                    if (chat && name is not ("text" or "translate" or "fallback" or "with" or "extra" or "color" or "shadow_color" or "bold"
                        or "italic" or "underlined" or "strikethrough" or "obfuscated" or "insertion" or ""))
                        throw new UnsupportedComponentHashException();
                    if (chat && name == "" && next is not (8 or 9 or 10))
                        throw new UnsupportedComponentHashException();

                    fields.Add((name, boolean && next == 1 ? H.Boolean(this.ReadBoolean()) : this.HashNbt(next, chat)));
                }
                // Component's persistent codec collapses an unstyled literal to a string.
                return chat && fields.Count == 1 && fields[0].Key is "text" or "" ? fields[0].Value : Record(fields);
            case 7: case 11: case 12:
                var count = this.ReadInt();
                var width = tag == 7 ? 1 : tag == 11 ? 4 : 8;
                var bytes = new byte[checked(count * width + 2)];
                bytes[0] = (byte)(tag == 7 ? 14 : tag == 11 ? 16 : 18);
                bytes[^1] = (byte)(bytes[0] + 1);
                for (var i = 0; i < count; i++)
                {
                    var bits = tag == 7 ? this.ReadByte() : tag == 11 ? this.ReadInt() : this.ReadLong();
                    for (var j = 0; j < width; j++)
                        bytes[1 + i * width + j] = (byte)(bits >> (j * 8));
                }

                return H.Bytes(bytes);
            default: throw new InvalidDataException("Invalid NBT hash tag.");
        }
    }

    private string ReadNbtString()
    {
        // Java modified UTF-8 encodes NUL as C0 80 and surrogate code units separately.
        var length = this.ReadUnsignedShort();
        var end = this.offset + length;
        var result = new StringBuilder();
        while (this.offset < end)
        {
            var first = this.ReadByte();
            result.Append((char)(first < 128 ? first : first < 224 ?
                ((first & 31) << 6) | (this.ReadByte() & 63) :
                ((first & 15) << 12) | ((this.ReadByte() & 63) << 6) | (this.ReadByte() & 63)));
        }

        return result.ToString();
    }
}
