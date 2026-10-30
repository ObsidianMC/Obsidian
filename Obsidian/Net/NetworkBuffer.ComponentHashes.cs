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
    /// Resolves a connection's registry ID to its resource location for component hashing, or null if it doesn't know
    /// it. Supply this for a client connected to another server, naming the entries of the server's datapack
    /// registries (its RegistryData): their IDs are the server's own, so an enchantment, banner pattern or armor trim
    /// it doesn't name produces an omitted hash. Built-in registries' entries it doesn't name take this process's
    /// names.
    /// </summary>
    public Func<string, int, string?>? ComponentRegistryName { get; set; }

    /// <summary>Hashes a component with vanilla's persistent codec/HashOps format, not its network bytes.
    /// False means the value needs an unsupported codec or an unavailable registry entry.</summary>
    public bool TryHashDataComponent(DataComponent component, out int hash)
    {
        if (component is PluginDataComponent plugin)
        {
            hash = plugin.TryHash(out var typed) ? typed : HashPluginComponent(plugin);
            return true;
        }

        var buffer = new NetworkBuffer { ComponentRegistryName = this.ComponentRegistryName, IdMaps = this.IdMaps };
        try
        {
            buffer.WriteDataComponent(component);
        }
        catch (MissingRegistryEntryException)
        {
            // The value names an entry the connection lacks; the hash it expects can't be known.
            hash = 0;
            return false;
        }

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
        // Like WriteItemStack, an item or removed component type the connection lacks makes an empty stack. A patched
        // component of a type it lacks is left out like one that can't be hashed: vanilla resynchronizes the slot.
        var itemId = stack is null ? -1 : this.RemoteItemId(stack.Holder.Id);
        if (stack is null
            || stack.IsAir
            || stack.Count <= 0
            || itemId < 0
            || !stack.RemoveComponents.All(this.HasComponentType))
        {
            this.WriteBoolean(false);
            return;
        }

        var hashes = new List<(DataComponentType Type, int Hash)>();
        foreach (var component in SentPatch(stack))
        {
            if (this.HasComponentType(component.Type) && this.TryHashDataComponent(component, out var hash))
                hashes.Add((component.Type, hash));
        }

        this.WriteBoolean(true);
        this.WriteVarInt(itemId);
        this.WriteVarInt(stack.Count);
        this.WriteVarInt(hashes.Count);
        foreach (var (type, hash) in hashes)
        {
            this.WriteComponentType(type);
            this.WriteInt(hash);
        }

        this.WriteVarInt(stack.RemoveComponents.Count);
        foreach (var type in stack.RemoveComponents)
            this.WriteComponentType(type);
    }

    /// <summary>Writes already computed hashes, for relaying or capturing ContainerClick values.</summary>
    public void WriteHashedItemStack(IHashedItemStack? stack)
    {
        var itemId = stack is null ? -1 : this.RemoteItemId(stack.Holder.Id);
        var writable = stack is not null
            && itemId >= 0
            && stack.ComponentsToRemove.All(this.HasComponentType);

        this.WriteBoolean(writable);
        if (!writable)
            return;

        var hashes = stack!.HashedComponents.Where(entry => this.HasComponentType(entry.Key)).ToList();

        this.WriteVarInt(itemId);
        this.WriteVarInt(stack.Count);
        this.WriteVarInt(hashes.Count);
        foreach (var (type, hash) in hashes)
        {
            this.WriteComponentType(type);
            this.WriteInt(hash);
        }

        this.WriteVarInt(stack.ComponentsToRemove.Count);
        foreach (var type in stack.ComponentsToRemove)
            this.WriteComponentType(type);
    }

    private sealed class ReceivedHashedStack(Item holder, int count, Func<string, int, string?>? registryNames) : IHashedItemStack
    {
        public Dictionary<DataComponentType, int> HashedComponents { get; } = [];
        public List<DataComponentType> ComponentsToRemove { get; } = [];
        public int Count { get; set; } = count;
        public Item Holder { get; } = holder;

        public bool Compare(ItemStack other)
        {
            if (other is null || this.Count != other.Count || this.Holder != other.Holder
                || !this.ComponentsToRemove.ToHashSet().SetEquals(other.RemoveComponents))
            {
                return false;
            }

            var patch = SentPatch(other);
            if (patch.Count != this.HashedComponents.Count)
                return false;

            var hasher = new NetworkBuffer { ComponentRegistryName = registryNames };
            return patch.All(component => this.HashedComponents.TryGetValue(component.Type, out var received)
                && hasher.TryHashDataComponent(component, out var expected) && received == expected);
        }
    }

    private sealed class UnsupportedComponentHashException : Exception;

    // Fallback for NBT-shaped codecs. Saved NBT cannot distinguish codec booleans from bytes; those need a typed hash.
    private static int HashPluginComponent(PluginDataComponent component)
    {
        var buffer = new NetworkBuffer();
        buffer.WriteNbtCompound(component.Saved);
        buffer.Reset();

        // The compound wrapping the value, then the value's tag type and name.
        buffer.ReadByte();
        var tag = buffer.ReadByte();
        buffer.ReadNbtString();

        return buffer.HashNbt(tag, false);
    }

    private string RegistryName(string registry, int id)
    {
        if (this.ComponentRegistryName?.Invoke(registry, id) is string name)
            return name;

        // Built-in registries have fixed entries, named here by this process's names, so a connection's ID is
        // translated first (see IdMaps). Datapack registries' entries are the connection's own: only without a
        // resolver, when this process is the server, are they this process's (vanilla's) enchantments.
        var local = this.IdMaps is RegistryIdMaps maps ? maps.Get(registry).ToLocal(id) : id;
        if (local < 0)
            throw new UnsupportedComponentHashException();

        return registry switch
        {
            "minecraft:potion" when Enum.IsDefined((Potion)local) =>
                "minecraft:" + ((Potion)local).ToString().ToSnakeCase(),
            "minecraft:mob_effect" when Enum.IsDefined((MobEffect)local) =>
                "minecraft:" + ((MobEffect)local).ToString().ToSnakeCase(),
            "minecraft:item" => ItemsRegistry.Get(local).UnlocalizedName,
            "minecraft:enchantment"
                when this.ComponentRegistryName is null
                && local < EnchantmentsRegistry.All.Count => EnchantmentsRegistry.All[local].Identifier,
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
    private static readonly string[] ConsumeEffectNames = ["apply_effects", "remove_effects", "clear_all_effects", "teleport_randomly", "play_sound"];
    private static readonly string[] PotFaceNames = ["back", "left", "right", "front"];
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
            case DataComponentType.DyedColor: return H.Int(this.ReadInt());
            case DataComponentType.MinimumAttackCharge: case DataComponentType.PotionDurationScale: return H.Float(this.ReadSingle());
            case DataComponentType.Unbreakable: case DataComponentType.Glider: case DataComponentType.Waxed: return H.Record();
            case DataComponentType.EnchantmentGlintOverride: return H.Boolean(this.ReadBoolean());
            case DataComponentType.ItemModel: case DataComponentType.TooltipStyle: case DataComponentType.NoteBlockSound:
                return H.String(this.ReadString());
            case DataComponentType.Rarity: return H.String(RarityNames[this.ReadVarInt()]);
            case DataComponentType.Dye: case DataComponentType.CushionColor:
            case DataComponentType.BaseColor: case DataComponentType.WolfCollar: case DataComponentType.CatCollar:
            case DataComponentType.SheepColor: case DataComponentType.ShulkerColor:
            case DataComponentType.TropicalFishBaseColor: case DataComponentType.TropicalFishPatternColor:
                return H.String(((Dye)this.ReadVarInt()).ToString().ToSnakeCase());
            case DataComponentType.Enchantable: return H.Record(("value", H.Int(this.ReadVarInt())));
            case DataComponentType.DamageResistant: return H.Record(("types", this.HashIdSet("minecraft:damage_type")));
            case DataComponentType.ProvidesBannerPatterns: return this.HashIdSet("minecraft:banner_pattern");
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

                var hidden = this.HashList(() => H.String(OpaqueDataComponent.GetIdentifier(this.ReadComponentType())));
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
            case DataComponentType.AttackAnimation: case DataComponentType.InteractAnimation:
                var swing = new List<(string, int)>();
                var animation = this.ReadVarInt();
                var ticks = this.ReadVarInt();
                if (animation != 1)
                    swing.Add(("type", H.String(SwingNames[animation])));
                if (ticks != 6)
                    swing.Add(("duration", H.Int(ticks)));

                return Record(swing);
            case DataComponentType.UseRemainder: return this.HashItemTemplate();
            case DataComponentType.ChargedProjectiles: case DataComponentType.BundleContents:
                return H.List(this.HashList(this.HashItemTemplate));
            case DataComponentType.Container:
                var slots = new List<int>();
                var slotCount = this.ReadComponentCount();
                for (var i = 0; i < slotCount; i++)
                {
                    if (this.ReadBoolean())
                        slots.Add(H.Record(("slot", H.Int(i)), ("item", this.HashItemTemplate())));
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
                return H.Record(("material", this.HashTrimMaterial()), ("pattern", this.HashTrimPattern()));
            case DataComponentType.ProvidesTrimMaterial:
                return this.HashTrimMaterial();
            case DataComponentType.Instrument:
                return this.HashInstrument();
            case DataComponentType.Consumable:
                var consumable = new List<(string, int)>();
                var consumeSeconds = this.ReadSingle();
                if (consumeSeconds != 1.6f)
                    consumable.Add(("consume_seconds", H.Float(consumeSeconds)));
                var consumeAnimation = this.ReadVarInt();
                if (consumeAnimation != 1)
                    consumable.Add(("animation", H.String(((ItemAnimation)consumeAnimation).ToString().ToSnakeCase())));
                var consumeSound = this.HashSound();
                if (consumeSound != H.String("minecraft:entity.generic.eat"))
                    consumable.Add(("sound", consumeSound));
                if (!this.ReadBoolean())
                    consumable.Add(("has_consume_particles", H.Boolean(false)));
                var consumeEffects = this.HashList(this.HashConsumeEffect);
                if (consumeEffects.Length > 0)
                    consumable.Add(("on_consume_effects", H.List(consumeEffects)));
                return Record(consumable);
            case DataComponentType.DeathProtection:
                return H.Record(("death_effects", H.List(this.HashList(this.HashConsumeEffect))));
            case DataComponentType.PotDecorations:
                var faces = new List<(string, int)>();
                foreach (var face in PotFaceNames)
                {
                    if (this.ReadBoolean())
                        faces.Add((face, this.HashItemTemplate()));
                }

                return Record(faces);
            case DataComponentType.SulfurCubeContent: return this.HashItemTemplate();
            case DataComponentType.VillagerFood: return H.Record(("nutrition", H.Int(this.ReadVarInt())));
            case DataComponentType.BlockTransformer: return this.RegistryHash("minecraft:block_transformer");
            case DataComponentType.ProvidesPotteryPattern: return this.RegistryHash("minecraft:decorated_pot_pattern");
            case DataComponentType.CatSoundVariant: return this.RegistryHash("minecraft:cat_sound_variant");
            case DataComponentType.ChickenSoundVariant: return this.RegistryHash("minecraft:chicken_sound_variant");
            case DataComponentType.CowSoundVariant: return this.RegistryHash("minecraft:cow_sound_variant");
            case DataComponentType.PigSoundVariant: return this.RegistryHash("minecraft:pig_sound_variant");
            case DataComponentType.Compostable: return H.Record(("layers", this.HashResolvable(false)));
            case DataComponentType.CookingFuel: case DataComponentType.BrewingFuel:
                return H.Record((type == DataComponentType.CookingFuel ? "burn_time" : "uses", this.HashResolvable(false)),
                    ("speed_multiplier", this.HashResolvable(true)));
            case DataComponentType.MobVisibility:
                return H.Record(("targeting_entity_types", this.HashIdSet("minecraft:entity_type")),
                    ("visibility", H.Float(this.ReadSingle())));
            case DataComponentType.SignTextFront: case DataComponentType.SignTextBack:
                var sign = new List<(string, int)> { ("messages", this.HashSignMessages()) };
                if (this.ReadBoolean())
                    sign.Add(("filtered_messages", this.HashSignMessages()));
                sign.Add(("color", H.String(((Dye)this.ReadVarInt()).ToString().ToSnakeCase())));
                sign.Add(("has_glowing_text", H.Boolean(this.ReadBoolean())));
                return Record(sign);
            default: throw new UnsupportedComponentHashException();
        }
    }

    private int HashSound()
    {
        var holder = this.ReadVarInt();
        if (holder != 0)
            return H.String(this.RegistryName("minecraft:sound_event", holder - 1));
        var fields = new List<(string, int)> { ("sound_id", H.String(this.ReadString())) };
        if (this.ReadBoolean())
            fields.Add(("range", H.Float(this.ReadSingle())));
        return Record(fields);
    }

    private int HashTrimMaterial()
    {
        var holder = this.ReadVarInt();
        return holder != 0 ? H.String(this.RegistryName("minecraft:trim_material", holder - 1))
            : H.Record(("palette_id", H.String(this.ReadString())), ("description", this.HashNbt(true)));
    }

    private int HashTrimPattern()
    {
        var holder = this.ReadVarInt();
        if (holder != 0)
            return H.String(this.RegistryName("minecraft:trim_pattern", holder - 1));
        var fields = new List<(string, int)> { ("asset_id", H.String(this.ReadString())), ("description", this.HashNbt(true)) };
        if (this.ReadBoolean())
            fields.Add(("decal", H.Boolean(true)));
        return Record(fields);
    }

    private int HashInstrument()
    {
        var holder = this.ReadVarInt();
        if (holder != 0)
            return H.String(this.RegistryName("minecraft:instrument", holder - 1));
        var fields = new List<(string, int)>
        {
            ("sound_event", this.HashSound()), ("use_duration", H.Float(this.ReadSingle())), ("range", H.Float(this.ReadSingle()))
        };
        var damage = this.ReadVarInt();
        if (damage != 0)
            fields.Add(("durability_damage", H.Int(damage)));
        fields.Add(("description", this.HashNbt(true)));
        return Record(fields);
    }

    private int HashConsumeEffect()
    {
        var type = this.ReadVarInt();
        var fields = new List<(string, int)> { ("type", H.String("minecraft:" + ConsumeEffectNames[type])) };
        switch (type)
        {
            case 0:
                fields.Add(("effects", H.List(this.HashList(() =>
                {
                    var id = this.RegistryHash("minecraft:mob_effect");
                    var effect = this.HashEffectDetails();
                    effect.Add(("id", id));
                    return Record(effect);
                }))));
                var probability = this.ReadSingle();
                if (probability != 1)
                    fields.Add(("probability", H.Float(probability)));
                break;
            case 1:
                fields.Add(("effects", this.HashIdSet("minecraft:mob_effect")));
                break;
            case 3:
                var diameter = this.ReadSingle();
                if (diameter != 16)
                    fields.Add(("diameter", H.Float(diameter)));
                if (!this.ReadBoolean())
                    fields.Add(("directional_particles", H.Boolean(false)));
                break;
            case 4:
                fields.Add(("sound", this.HashSound()));
                break;
        }
        return Record(fields);
    }

    private int HashResolvable(bool floating) => this.ReadBoolean()
        ? floating ? H.Float(this.ReadSingle()) : H.Int(this.ReadInt())
        : H.String(this.ReadString());

    private int HashSignMessages() => H.List(Enumerable.Range(0, 4).Select(_ => this.HashNbt(true)).ToArray());

    private int HashItemTemplate()
    {
        var item = this.RegistryHash("minecraft:item");
        var count = this.ReadVarInt();
        var fields = new List<(string, int)> { ("id", item) };
        if (count != 1)
            fields.Add(("count", H.Int(count)));
        this.HashStackPatch(fields);
        return Record(fields);
    }

    private void HashStackPatch(List<(string, int)> fields)
    {
        var added = this.ReadComponentCount();
        var removed = this.ReadComponentCount();
        var patch = new List<(int, int)>();
        for (var i = 0; i < added; i++)
        {
            var type = this.ReadComponentType();
            patch.Add((H.String(OpaqueDataComponent.GetIdentifier(type)), this.HashComponent(type)));
        }

        for (var i = 0; i < removed; i++)
            patch.Add((H.String("!" + OpaqueDataComponent.GetIdentifier(this.ReadComponentType())), H.Record()));

        if (patch.Count > 0)
            fields.Add(("components", H.Map(patch)));
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
                    if (chat && name is not ("text" or "translate" or "keybind" or "fallback" or "with" or "extra" or "color" or "shadow_color" or "bold"
                        or "italic" or "underlined" or "strikethrough" or "obfuscated" or "insertion" or ""))
                    {
                        throw new UnsupportedComponentHashException();
                    }

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
