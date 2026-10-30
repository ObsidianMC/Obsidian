using System.IO;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using System.Runtime.CompilerServices;

namespace Obsidian.Net;

public partial class NetworkBuffer
{
    // Wire shapes from vanilla 26.3 DataComponents and its stream codecs. These consume values without
    // resolving server-owned registries. A holder ID belongs to the connection, not Obsidian's registry order.
    // Keeping framing separate from the display models also preserves NBT/chat encodings the models don't express.
    private static readonly ConditionalWeakTable<DataComponent, ComponentEncoding> ComponentEncodings = new();
    private sealed record ComponentEncoding(byte[] Original, byte[] Normalized);
    private int componentDepth;

    /// <summary>
    /// The components of a stack's patch that clients get: vanilla's, and plugins' whose type has a network codec,
    /// while its plugin accepts calls. Types without one are the server's own, like vanilla's unsynced components.
    /// </summary>
    /// <remarks>
    /// Vanilla clients can't join while a plugin's type has a network codec (see SyncedRegistries), so they never get
    /// a plugin's component.
    /// </remarks>
    internal static List<DataComponent> SentPatch(ItemStack stack) =>
    [
        .. stack.ProjectComponents(stack.Patch)
            .Select(component => component is PluginDataComponent plugin ? plugin.ForNetwork() : component)
            .OfType<DataComponent>()
    ];

    public DataComponent ReadDataComponent(DataComponentType type)
    {
        // A plugin's component, read with its type's network codec and kept in its saved form. Without the codec, its
        // bytes can't be told apart from what follows them.
        if (DataComponentTypesRegistry.TryGet(type, out var plugin))
        {
            using var call = DataComponentTypesRegistry.TryEnter(type, out var binding);
            var read = binding?.ReadNetwork
                ?? throw new InvalidDataException($"{plugin.Identifier} isn't read from the network.");

            return new PluginDataComponent(type, plugin.Identifier, read(this));
        }

        var start = this.offset;
        this.SkipComponent(type);
        var bytes = this.data.AsSpan(start, this.offset - start).ToArray();
        var component = ComponentBuilder.Create(type);
        if (component is null)
            return new OpaqueDataComponent(type, [.. bytes]);

        try
        {
            var reader = new NetworkBuffer(bytes) { componentDepth = this.componentDepth, IdMaps = this.IdMaps };
            component = component.Read(reader);
            if (reader.Offset != bytes.Length)
                throw new InvalidDataException($"The {type} model consumed {reader.Offset} of {bytes.Length} bytes.");

            var normalized = new NetworkBuffer { IdMaps = this.IdMaps };
            component.Write(normalized);
            ComponentEncodings.Add(component, new(bytes, normalized.data.AsSpan(0, normalized.Offset).ToArray()));
            return component;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException
            or IOException or IndexOutOfRangeException or OverflowException or System.Diagnostics.UnreachableException)
        {
            // E.g. valid custom NBT with empty compound keys cannot be expressed by the public NBT model.
            // Framing has already been checked. Retain the complete value instead of rejecting the enclosing packet.
            return new OpaqueDataComponent(type, [.. bytes]);
        }
    }

    public void WriteDataComponent(DataComponent component)
    {
        if (ComponentEncodings.TryGetValue(component, out var encoding))
        {
            var current = new NetworkBuffer { IdMaps = this.IdMaps };
            component.Write(current);
            this.Write(current.data.AsSpan(0, current.Offset).SequenceEqual(encoding.Normalized)
                ? encoding.Original : current.data.AsSpan(0, current.Offset));
        }
        else
        {
            component.Write(this);
        }
    }

    private int ReadComponentCount()
    {
        var count = this.ReadVarInt();
        if (count < 0 || count > 65536)
            throw new InvalidDataException($"Invalid component collection size: {count}.");

        return count;
    }

    private void SkipBytes(int count)
    {
        if (count < 0 || count > this.size - this.offset)
            throw new EndOfStreamException("Truncated component value.");

        this.offset += count;
        this.BytesPending -= count;
    }

    private int ReadComponentLength()
    {
        var length = this.ReadVarInt();
        if (length < 0 || length > this.size - this.offset)
            throw new InvalidDataException("Invalid component byte length.");

        return length;
    }

    private void SkipString() => this.SkipBytes(this.ReadComponentLength());

    private void SkipList(Action read)
    {
        var count = this.ReadComponentCount();
        for (var i = 0; i < count; i++)
            read();
    }

    private void SkipOptional(Action read)
    {
        if (this.ReadBoolean())
            read();
    }

    private void SkipVarInt() => this.ReadVarInt();

    private void SkipHolder(Action inline)
    {
        if (this.ReadVarInt() == 0)
            inline();
    }

    private void SkipIdSet()
    {
        var count = this.ReadComponentCount();
        if (count == 0)
        {
            this.SkipString();
        }
        else
        {
            for (var i = 1; i < count; i++)
                this.ReadVarInt();
        }
    }

    private void SkipSound() => this.SkipHolder(() =>
    {
        this.SkipString();
        this.SkipOptional(() => this.SkipBytes(4));
    });

    private void SkipTrimMaterial() => this.SkipHolder(() =>
    {
        this.SkipString();
        this.SkipNbt();
    });

    private void SkipEither(Action left, Action right)
    {
        if (this.ReadBoolean())
            left();
        else
            right();
    }

    private void SkipFilterable(Action value)
    {
        value();
        this.SkipOptional(value);
    }

    private void SkipEffectDetails()
    {
        if (++this.componentDepth > 64)
            throw new InvalidDataException("Component nesting exceeds 64.");

        try
        {
            this.ReadVarInt();
            this.ReadVarInt();
            this.SkipBytes(3);
            this.SkipOptional(this.SkipEffectDetails);
        }
        finally
        {
            this.componentDepth--;
        }
    }

    private void SkipEffect()
    {
        this.ReadVarInt();
        this.SkipEffectDetails();
    }

    private void SkipConsumeEffect()
    {
        switch (this.ReadVarInt())
        {
            case 0:
                this.SkipList(this.SkipEffect);
                this.SkipBytes(4);
                break;
            case 1:
                this.SkipIdSet();
                break;
            case 2:
                break;
            case 3:
                this.SkipBytes(5);
                break;
            case 4:
                this.SkipSound();
                break;
            default:
                throw new InvalidDataException("Unknown consume effect.");
        }
    }

    private void SkipExplosion()
    {
        this.ReadVarInt();
        this.SkipList(() => this.SkipBytes(4));
        this.SkipList(() => this.SkipBytes(4));
        this.SkipBytes(2);
    }

    private void SkipComponent(DataComponentType type)
    {
        if (++this.componentDepth > 64)
            throw new InvalidDataException("Component nesting exceeds 64.");

        try
        {
            this.SkipComponentValue(type);
        }
        finally
        {
            this.componentDepth--;
        }
    }

    private void SkipComponentValue(DataComponentType type)
    {
        switch (type)
        {
            case DataComponentType.CustomData:
            case DataComponentType.IntangibleProjectile:
            case DataComponentType.MapDecorations:
            case DataComponentType.DebugStickState:
            case DataComponentType.BucketEntityData:
            case DataComponentType.Recipes:
            case DataComponentType.Lock:
            case DataComponentType.ContainerLoot:
            case DataComponentType.CustomName:
            case DataComponentType.ItemName:
                this.SkipNbt();
                break;
            case DataComponentType.Unbreakable:
            case DataComponentType.CreativeSlotLock:
            case DataComponentType.Glider:
            case DataComponentType.Waxed:
                break;
            case DataComponentType.DamageType:
            case DataComponentType.ChickenVariant:
            case DataComponentType.ZombieNautilusVariant:
            case DataComponentType.AdditionalTradeCost:
            case DataComponentType.VillagerFood:
            case DataComponentType.Dye:
            case DataComponentType.CushionColor:
            case DataComponentType.BlockTransformer:
            case DataComponentType.ProvidesPotteryPattern:
            case DataComponentType.CatSoundVariant:
            case DataComponentType.ChickenSoundVariant:
            case DataComponentType.CowSoundVariant:
            case DataComponentType.PigSoundVariant:
            case DataComponentType.MaxStackSize:
            case DataComponentType.MaxDamage:
            case DataComponentType.Damage:
            case DataComponentType.Rarity:
            case DataComponentType.RepairCost:
            case DataComponentType.Enchantable:
            case DataComponentType.MapId:
            case DataComponentType.MapPostProcessing:
            case DataComponentType.OminousBottleAmplifier:
            case DataComponentType.BaseColor:
            case DataComponentType.VillagerVariant:
            case DataComponentType.WolfVariant:
            case DataComponentType.WolfSoundVariant:
            case DataComponentType.WolfCollar:
            case DataComponentType.FoxVariant:
            case DataComponentType.SalmonSize:
            case DataComponentType.ParrotVariant:
            case DataComponentType.TropicalFishPattern:
            case DataComponentType.TropicalFishBaseColor:
            case DataComponentType.TropicalFishPatternColor:
            case DataComponentType.MooshroomVariant:
            case DataComponentType.RabbitVariant:
            case DataComponentType.PigVariant:
            case DataComponentType.CowVariant:
            case DataComponentType.FrogVariant:
            case DataComponentType.HorseVariant:
            case DataComponentType.LlamaVariant:
            case DataComponentType.AxolotlVariant:
            case DataComponentType.CatVariant:
            case DataComponentType.CatCollar:
            case DataComponentType.SheepColor:
            case DataComponentType.ShulkerColor:
                this.ReadVarInt();
                break;
            case DataComponentType.DyedColor:
            case DataComponentType.MinimumAttackCharge:
            case DataComponentType.PotionDurationScale:
                this.SkipBytes(4);
                break;
            case DataComponentType.UseEffects:
                this.SkipBytes(6);
                break;
            case DataComponentType.ItemModel:
            case DataComponentType.TooltipStyle:
            case DataComponentType.NoteBlockSound:
                this.SkipString();
                break;
            case DataComponentType.Lore:
                this.SkipList(this.SkipNbt);
                break;
            case DataComponentType.Enchantments:
            case DataComponentType.StoredEnchantments:
            case DataComponentType.SuspiciousStewEffects:
                this.SkipList(this.SkipVarIntPair);
                break;
            case DataComponentType.CanPlaceOn:
            case DataComponentType.CanBreak:
                this.SkipList(this.SkipBlockPredicate);
                break;
            case DataComponentType.AttributeModifiers:
                this.SkipList(this.SkipAttributeModifier);
                break;
            case DataComponentType.CustomModelData:
                this.SkipCustomModelData();
                break;
            case DataComponentType.TooltipDisplay:
                this.SkipBytes(1);
                this.SkipList(this.SkipVarInt);
                break;
            case DataComponentType.EnchantmentGlintOverride:
                this.SkipBytes(1);
                break;
            case DataComponentType.Food:
                this.ReadVarInt();
                this.SkipBytes(5);
                break;
            case DataComponentType.Consumable:
                this.SkipConsumable();
                break;
            case DataComponentType.UseRemainder:
                this.SkipItemTemplate();
                break;
            case DataComponentType.UseCooldown:
                this.SkipBytes(4);
                this.SkipOptional(this.SkipString);
                break;
            case DataComponentType.Tool:
                this.SkipTool();
                break;
            case DataComponentType.Weapon:
                this.ReadVarInt();
                this.SkipBytes(4);
                break;
            case DataComponentType.AttackRange:
                this.SkipBytes(24);
                break;
            case DataComponentType.Equippable:
                this.SkipEquippable();
                break;
            case DataComponentType.Repairable:
            case DataComponentType.DamageResistant:
            case DataComponentType.ProvidesBannerPatterns:
                this.SkipIdSet();
                break;
            case DataComponentType.DeathProtection:
                this.SkipList(this.SkipConsumeEffect);
                break;
            case DataComponentType.BlocksAttacks:
                this.SkipBlocksAttacks();
                break;
            case DataComponentType.PiercingWeapon:
                this.SkipPiercingWeapon();
                break;
            case DataComponentType.KineticWeapon:
                this.SkipKineticWeapon();
                break;
            // 26.1 split swing_animation into these two, which keep its codec.
            case DataComponentType.AttackAnimation:
            case DataComponentType.InteractAnimation:
                this.SkipVarIntPair();
                break;
            case DataComponentType.ChargedProjectiles:
            case DataComponentType.BundleContents:
                this.SkipList(this.SkipItemTemplate);
                break;
            case DataComponentType.Container:
                this.SkipList(this.SkipOptionalItemTemplate);
                break;
            case DataComponentType.PotionContents:
                this.SkipPotionContents();
                break;
            case DataComponentType.WritableBookContent:
                this.SkipList(this.SkipFilterableString);
                break;
            case DataComponentType.WrittenBookContent:
                this.SkipWrittenBookContent();
                break;
            case DataComponentType.Trim:
                this.SkipTrimMaterial();
                this.SkipHolder(this.SkipInlineTrimPattern);
                break;
            case DataComponentType.EntityData:
            case DataComponentType.BlockEntityData:
                this.ReadVarInt();
                this.SkipNbt();
                break;
            case DataComponentType.Instrument:
                this.SkipHolder(this.SkipInlineInstrument);
                break;
            case DataComponentType.ProvidesTrimMaterial:
                this.SkipTrimMaterial();
                break;
            case DataComponentType.JukeboxPlayable:
                this.SkipHolder(this.SkipInlineJukeboxSong);
                break;
            case DataComponentType.LodestoneTracker:
                this.SkipOptional(this.SkipGlobalPosition);
                this.SkipBytes(1);
                break;
            case DataComponentType.FireworkExplosion:
                this.SkipExplosion();
                break;
            case DataComponentType.Fireworks:
                this.ReadVarInt();
                this.SkipList(this.SkipExplosion);
                break;
            case DataComponentType.Profile:
                this.SkipProfile();
                break;
            case DataComponentType.BannerPatterns:
                this.SkipList(this.SkipBannerPatternLayer);
                break;
            case DataComponentType.PotDecorations:
                this.SkipPotDecorations();
                break;
            case DataComponentType.SulfurCubeContent:
                this.SkipItemTemplate();
                break;
            case DataComponentType.Compostable:
                this.SkipFloatOrString();
                break;
            case DataComponentType.CookingFuel:
            case DataComponentType.BrewingFuel:
                this.SkipFloatOrString();
                this.SkipFloatOrString();
                break;
            case DataComponentType.MobVisibility:
                this.SkipIdSet();
                this.SkipBytes(4);
                break;
            case DataComponentType.SignTextFront:
            case DataComponentType.SignTextBack:
                this.SkipSignText();
                break;
            case DataComponentType.BlockState:
                this.SkipList(this.SkipStringPair);
                break;
            case DataComponentType.Bees:
                this.SkipList(this.SkipBee);
                break;
            case DataComponentType.BreakSound:
                this.SkipSound();
                break;
            case DataComponentType.PaintingVariant:
                this.SkipHolder(this.SkipInlinePaintingVariant);
                break;
            case var _ when DataComponentTypesRegistry.TryGet(type, out _):
                // A plugin's component in a nested stack: its codec is the only way past it.
                this.ReadDataComponent(type);
                break;
            default:
                throw new InvalidDataException($"Unknown 26.3 component ID {(int)type}.");
        }
    }

    private void SkipVarIntPair()
    {
        this.ReadVarInt();
        this.ReadVarInt();
    }

    private void SkipStringPair()
    {
        this.SkipString();
        this.SkipString();
    }

    private void SkipFloatOrString() => this.SkipEither(() => this.SkipBytes(4), this.SkipString);

    private void SkipFilterableString() => this.SkipFilterable(this.SkipString);

    private void SkipOptionalItemTemplate() => this.SkipOptional(this.SkipItemTemplate);

    private void SkipBlockPredicate()
    {
        this.SkipOptional(this.SkipIdSet);
        this.SkipOptional(() => this.SkipList(this.SkipBlockPropertyMatcher));
        this.SkipOptional(this.SkipNbt);
        this.SkipList(() => this.SkipComponent(this.ReadComponentType()));
        this.SkipList(this.SkipPartialComponentPredicate);
    }

    private void SkipBlockPropertyMatcher()
    {
        this.SkipString();
        this.SkipEither(this.SkipString, this.SkipPropertyRange);
    }

    private void SkipPropertyRange()
    {
        this.SkipOptional(this.SkipString);
        this.SkipOptional(this.SkipString);
    }

    private void SkipPartialComponentPredicate()
    {
        this.SkipBytes(1);
        this.ReadVarInt();
        this.SkipNbt();
    }

    private void SkipAttributeModifier()
    {
        this.ReadVarInt();
        this.SkipString();
        this.SkipBytes(8);
        this.ReadVarInt();
        this.ReadVarInt();

        var display = this.ReadVarInt();
        if (display == 2)
            this.SkipNbt();
        else if (display is not (0 or 1))
            throw new InvalidDataException("Unknown attribute display.");
    }

    private void SkipCustomModelData()
    {
        this.SkipList(() => this.SkipBytes(4));
        this.SkipList(() => this.SkipBytes(1));
        this.SkipList(this.SkipString);
        this.SkipList(() => this.SkipBytes(4));
    }

    private void SkipConsumable()
    {
        this.SkipBytes(4);
        this.ReadVarInt();
        this.SkipSound();
        this.SkipBytes(1);
        this.SkipList(this.SkipConsumeEffect);
    }

    private void SkipTool()
    {
        this.SkipList(this.SkipToolRule);
        this.SkipBytes(4);
        this.ReadVarInt();
        this.SkipBytes(1);
    }

    private void SkipToolRule()
    {
        this.SkipIdSet();
        this.SkipOptional(() => this.SkipBytes(4));
        this.SkipOptional(() => this.SkipBytes(1));
    }

    private void SkipEquippable()
    {
        this.ReadVarInt();
        this.SkipSound();
        this.SkipOptional(this.SkipString);
        this.SkipOptional(this.SkipString);
        this.SkipOptional(this.SkipIdSet);
        this.SkipBytes(5);
        this.SkipSound();
    }

    private void SkipBlocksAttacks()
    {
        this.SkipBytes(8);
        this.SkipList(this.SkipDamageReduction);
        this.SkipBytes(12);
        this.SkipOptional(this.SkipString);
        this.SkipOptional(this.SkipSound);
        this.SkipOptional(this.SkipSound);
    }

    private void SkipDamageReduction()
    {
        this.SkipBytes(4);
        this.SkipOptional(this.SkipIdSet);
        this.SkipBytes(8);
    }

    private void SkipPiercingWeapon()
    {
        this.SkipBytes(2);
        this.SkipOptional(this.SkipSound);
        this.SkipOptional(this.SkipSound);
    }

    private void SkipKineticWeapon()
    {
        this.ReadVarInt();
        this.ReadVarInt();
        for (var i = 0; i < 3; i++)
            this.SkipOptional(this.SkipKineticCondition);

        this.SkipBytes(8);
        this.SkipOptional(this.SkipSound);
        this.SkipOptional(this.SkipSound);
    }

    private void SkipKineticCondition()
    {
        this.ReadVarInt();
        this.SkipBytes(8);
    }

    private void SkipPotionContents()
    {
        this.SkipOptional(this.SkipVarInt);
        this.SkipOptional(() => this.SkipBytes(4));
        this.SkipList(this.SkipEffect);
        this.SkipOptional(this.SkipString);
    }

    private void SkipWrittenBookContent()
    {
        this.SkipFilterable(this.SkipString);
        this.SkipString();
        this.ReadVarInt();
        this.SkipList(() => this.SkipFilterable(this.SkipNbt));
        this.SkipBytes(1);
    }

    private void SkipInlineTrimPattern()
    {
        this.SkipString();
        this.SkipNbt();
        this.SkipBytes(1);
    }

    private void SkipInlineInstrument()
    {
        this.SkipSound();
        this.SkipBytes(8);
        this.ReadVarInt();
        this.SkipNbt();
    }

    private void SkipInlineJukeboxSong()
    {
        this.SkipSound();
        this.SkipNbt();
        this.SkipBytes(4);
        this.ReadVarInt();
    }

    private void SkipGlobalPosition()
    {
        this.SkipString();
        this.SkipBytes(8);
    }

    private void SkipProfile()
    {
        this.SkipEither(this.SkipResolvedProfileIdentity, this.SkipPartialProfileIdentity);
        this.SkipList(this.SkipProfileProperty);
        for (var i = 0; i < 3; i++)
            this.SkipOptional(this.SkipString);

        this.SkipOptional(this.SkipVarInt);
    }

    private void SkipResolvedProfileIdentity()
    {
        this.SkipBytes(16);
        this.SkipString();
    }

    private void SkipPartialProfileIdentity()
    {
        this.SkipOptional(this.SkipString);
        this.SkipOptional(() => this.SkipBytes(16));
    }

    private void SkipProfileProperty()
    {
        this.SkipString();
        this.SkipString();
        this.SkipOptional(this.SkipString);
    }

    private void SkipBannerPatternLayer()
    {
        this.SkipHolder(this.SkipStringPair);
        this.ReadVarInt();
    }

    private void SkipPotDecorations()
    {
        for (var i = 0; i < 4; i++)
            this.SkipOptional(this.SkipItemTemplate);
    }

    private void SkipSignText()
    {
        this.SkipSignMessages();
        this.SkipOptional(this.SkipSignMessages);
        this.ReadVarInt();
        this.SkipBytes(1);
    }

    private void SkipBee()
    {
        this.ReadVarInt();
        this.SkipNbt();
        this.ReadVarInt();
        this.ReadVarInt();
    }

    private void SkipInlinePaintingVariant()
    {
        this.ReadVarInt();
        this.ReadVarInt();
        this.SkipString();
        this.SkipOptional(this.SkipNbt);
        this.SkipOptional(this.SkipNbt);
    }

    private void SkipSignMessages()
    {
        for (var i = 0; i < 4; i++)
            this.SkipNbt();
    }

    // ItemStackTemplate puts the item ID before count and has no empty-stack sentinel.
    private void SkipItemTemplate()
    {
        this.ReadVarInt();
        if (this.ReadVarInt() <= 0)
            throw new InvalidDataException("Expected a nonempty nested item template.");
        var added = this.ReadComponentCount();
        var removed = this.ReadComponentCount();
        for (var i = 0; i < added; i++)
            this.SkipComponent(this.ReadComponentType());
        for (var i = 0; i < removed; i++)
            this.ReadComponentType();
    }

    private void SkipNbt() => this.SkipNbtPayload(this.ReadByte(), 0);

    private void SkipNbtPayload(int tag, int depth)
    {
        if (depth > 64)
            throw new InvalidDataException("NBT nesting exceeds 64.");

        switch (tag)
        {
            case 0:
                break;
            case 1:
                this.SkipBytes(1);
                break;
            case 2:
                this.SkipBytes(2);
                break;
            case 3: case 5:
                this.SkipBytes(4);
                break;
            case 4: case 6:
                this.SkipBytes(8);
                break;
            case 7:
                this.SkipBytes(this.ReadInt());
                break;
            case 8:
                this.SkipBytes(this.ReadUnsignedShort());
                break;
            case 9:
                var element = this.ReadByte();
                var count = this.ReadInt();
                if (count < 0 || count > 1048576 || (element == 0 && count != 0))
                    throw new InvalidDataException("Invalid NBT list.");

                for (var i = 0; i < count; i++)
                    this.SkipNbtPayload(element, depth + 1);
                break;
            case 10:
                int next;
                while ((next = this.ReadByte()) != 0)
                {
                    this.SkipBytes(this.ReadUnsignedShort());
                    this.SkipNbtPayload(next, depth + 1);
                }
                break;
            case 11:
                this.SkipBytes(checked(this.ReadInt() * 4));
                break;
            case 12:
                this.SkipBytes(checked(this.ReadInt() * 8));
                break;
            default:
                throw new InvalidDataException($"Invalid NBT tag {tag}.");
        }
    }
}
