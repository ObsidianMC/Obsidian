using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using System.IO;

namespace Obsidian.API.Utilities;

public partial class Extensions
{
    // The remaining modeled 1.21.11 DataComponents codecs. These are disk formats, not stream encodings:
    // enums and holders use names, Unit is {}, and nested stacks use ItemStack.CODEC recursively.
    private static INbtTag? AdditionalComponentToNbt(DataComponent component)
    {
        var name = component.Identifier;
        return component switch
        {
            SimpleDataComponent<int> when component.Type == DataComponentType.AdditionalTradeCost => null,
            SimpleDataComponent<int> number when component.Type == DataComponentType.VillagerFood =>
                new NbtCompound(name) { new NbtTag<int>("nutrition", number.Value) },
            SimpleDataComponent<ComponentIntValue> compost => new NbtCompound(name) { ComponentIntToNbt("layers", compost.Value) },
            SimpleDataComponent<MobVisibilityData> visibility => new NbtCompound(name)
            {
                SetToNbt("targeting_entity_types", visibility.Value!.TargetingEntityTypes, ComponentValueCodecs.EntityTypes),
                new NbtTag<float>("visibility", visibility.Value.Visibility)
            },
            SimpleDataComponent<FuelComponentData> fuel => new NbtCompound(name)
            {
                ComponentIntToNbt(component.Type == DataComponentType.CookingFuel ? "burn_time" : "uses", fuel.Value!.Amount),
                ComponentFloatToNbt("speed_multiplier", fuel.Value.SpeedMultiplier)
            },
            SimpleDataComponent<int> number when component.Type == DataComponentType.Enchantable =>
                new NbtCompound(name) { new NbtTag<int>("value", number.Value) },
            SimpleDataComponent<int> number => new NbtTag<int>(name, number.Value),
            SimpleDataComponent<bool> flag => new NbtTag<bool>(name, flag.Value),
            SimpleDataComponent marker => new NbtCompound(name),
            SimpleDataComponent<string> text => new NbtTag<string>(name, text.Value),
            SimpleDataComponent<NbtCompound> data => CopyCompound(name, data.Value ?? new NbtCompound()),
            SimpleDataComponent<ItemRarity> rarity => new NbtTag<string>(name, rarity.Value.ToString().ToSnakeCase()),
            SimpleDataComponent<Dye> dye => new NbtTag<string>(name, dye.Value.ToString().ToSnakeCase()),
            SimpleDataComponent<MapPostProcessingType> processing =>
                new NbtTag<string>(name, processing.Value.ToString().ToSnakeCase()),
            SimpleDataComponent<ImmutableArray<ChatMessage>> lore =>
                SavedList(name, lore.Value.Select(value => value.ToNbt())),
            SimpleDataComponent<ImmutableArray<string>> strings => SavedStrings(name, strings.Value),
            SimpleDataComponent<ImmutableArray<ItemStack>> stacks => ChargedProjectilesToNbt(name, stacks.Value),
            SimpleDataComponent<ItemStack?> stack when stack.Value is not null => stack.Value.ToNbt(name),
            SimpleDataComponent<ImmutableArray<BlockStateProperty>> state => StateToNbt(name, state.Value),
            SimpleDataComponent<ImmutableArray<Page>> book => new NbtCompound(name)
            {
                SavedList("pages", book.Value.Select(page => FilteredString(page.RawContent, page.FilteredContent)))
            },
            SimpleDataComponent<FireworkExplosion> explosion => ExplosionToNbt(name, explosion.Value),
            SimpleDataComponent<IdSet> repairable => new NbtCompound(name)
            {
                SetToNbt(component.Type == DataComponentType.DamageResistant ? "types" : "items", repairable.Value,
                    component.Type == DataComponentType.DamageResistant ? "minecraft:damage_type" : ComponentValueCodecs.Items)
            },
            SimpleDataComponent<ImmutableArray<IConsumeEffect>> protection => new NbtCompound(name)
            {
                SavedList("death_effects", protection.Value.Select(ConsumeEffectToNbt))
            },
            FoodDataComponent food => new NbtCompound(name)
            {
                new NbtTag<int>("nutrition", food.Nutrition),
                new NbtTag<float>("saturation", food.SaturationModifier),
                new NbtTag<bool>("can_always_eat", food.CanAlwaysEat)
            },
            UseCooldownDataComponent cooldown => CooldownToNbt(name, cooldown),
            ToolDataComponent tool => ToolToNbt(name, tool),
            ConsumableDataComponent consumable => new NbtCompound(name)
            {
                new NbtTag<float>("consume_seconds", consumable.ConsumeSeconds),
                new NbtTag<string>("animation", consumable.Animation.ToString().ToSnakeCase()),
                SoundToNbt("sound", consumable.Sound),
                new NbtTag<bool>("has_consume_particles", consumable.HasConsumeParticles),
                SavedList("on_consume_effects", consumable.Effects.Select(effect => ConsumeEffectToNbt(effect.Effect)))
            },
            EquippableDataComponent equipment => EquipmentToNbt(name, equipment),
            EntityDataComponent entity => EntityDataToNbt(name, entity),
            BeesDataComponent bees => SavedList(name, bees.Occupants.Select(occupant => new NbtCompound
            {
                EntityDataToNbt("entity_data", occupant.Entity),
                new NbtTag<int>("ticks_in_hive", occupant.TicksInHive),
                new NbtTag<int>("min_ticks_in_hive", occupant.MinTicksInHive)
            })),
            CustomModelDataComponent model => new NbtCompound(name)
            {
                SavedList("floats", model.Floats.Select(value => new NbtTag<float>(string.Empty, value))),
                SavedList("flags", model.Flags.Select(value => new NbtTag<bool>(string.Empty, value))),
                SavedStrings("strings", model.Strings),
                SavedList("colors", model.Colors.Select(value => new NbtTag<int>(string.Empty, value)))
            },
            LodestoneTrackerDataComponent tracker => LodestoneToNbt(name, tracker),
            MapDecorationDataComponent decorations => MapDecorationsToNbt(name, decorations),
            WrittenBookDataComponent book => new NbtCompound(name)
            {
                FilteredString(book.Title, book.FilteredTitle, "title"),
                new NbtTag<string>("author", book.Author),
                new NbtTag<int>("generation", book.Generation),
                SavedList("pages", book.Pages.Select(page => FilteredText(page.Raw, page.Filtered))),
                new NbtTag<bool>("resolved", book.Resolved)
            },
            BlockPredicatesDataComponent predicates => PredicatesToNbt(name, predicates),
            AttributeModifiersDataComponent attributes => AttributesToNbt(name, attributes),
            BannerPatternsDataComponent patterns => PatternsToNbt(name, patterns),
            TrimDataComponent trim => TrimToNbt(name, trim),
            ProfileDataComponent profile => ProfileToNbt(name, profile),
            JukeboxPlayableDataComponent song => SongToNbt(name, song),
            InstrumentDataComponent instrument => InstrumentToNbt(name, instrument),
            _ => null
        };
    }

    private static INbtTag ComponentIntToNbt(string name, ComponentIntValue value) => value.Provider is string provider
        ? new NbtTag<string>(name, provider) : new NbtTag<int>(name, value.Constant);

    private static INbtTag ComponentFloatToNbt(string name, ComponentFloatValue value) => value.Provider is string provider
        ? new NbtTag<string>(name, provider) : new NbtTag<float>(name, value.Constant);

    private static ComponentIntValue ComponentIntFromNbt(INbtTag value) => value is NbtTag<string> provider
        ? new(0, provider.Value) : new(((NbtTag<int>)value).Value);

    private static ComponentFloatValue ComponentFloatFromNbt(INbtTag value) => value is NbtTag<string> provider
        ? new(0, provider.Value) : new(((NbtTag<float>)value).Value);

    private static DataComponent? AdditionalComponentFromNbt(string name, INbtTag tag)
    {
        if (!componentTypesById.TryGetValue(name, out var type))
            return null;

        var prototype = ComponentBuilder.Create(type);
        if (prototype is SimpleDataComponent<int> integer)
        {
            if (type == DataComponentType.AdditionalTradeCost)
                return null;
            if (type == DataComponentType.VillagerFood && tag is NbtCompound foodValue)
                return integer with { Value = foodValue.GetInt("nutrition") };
            if (type == DataComponentType.Enchantable && tag is NbtCompound enchantable)
                return integer with { Value = enchantable.GetInt("value") };

            return tag is NbtTag<int> value ? integer with { Value = value.Value } : null;
        }

        if (prototype is SimpleDataComponent<MobVisibilityData> visibility && tag is NbtCompound visibilityValue)
            return visibility with { Value = new(SetFromNbt(visibilityValue["targeting_entity_types"], ComponentValueCodecs.EntityTypes),
                visibilityValue.GetFloat("visibility")) };
        if (prototype is SimpleDataComponent<ComponentIntValue> compost && tag is NbtCompound compostValue)
            return compost with { Value = ComponentIntFromNbt(compostValue["layers"]) };
        if (prototype is SimpleDataComponent<FuelComponentData> fuel && tag is NbtCompound fuelValue)
            return fuel with { Value = new(ComponentIntFromNbt(fuelValue[type == DataComponentType.CookingFuel ? "burn_time" : "uses"]),
                ComponentFloatFromNbt(fuelValue["speed_multiplier"])) };

        if (prototype is SimpleDataComponent<bool> boolean)
            return boolean with { Value = ReadBoolean(tag) };

        if (prototype is SimpleDataComponent marker)
            return marker;

        if (prototype is SimpleDataComponent<string> text)
        {
            return tag is NbtTag<string> value ? text with { Value = value.Value! } : null;
        }

        if (prototype is SimpleDataComponent<NbtCompound> data && tag is NbtCompound compound)
            return data with { Value = CopyCompound(string.Empty, compound) };

        if (prototype is SimpleDataComponent<ItemRarity> rarity && tag is NbtTag<string> rarityName)
            return rarity with { Value = SavedEnum<ItemRarity>(rarityName.Value!) };

        if (prototype is SimpleDataComponent<Dye> dye && tag is NbtTag<string> dyeName)
            return dye with { Value = SavedEnum<Dye>(dyeName.Value!) };

        if (prototype is SimpleDataComponent<MapPostProcessingType> processing && tag is NbtTag<string> processingName)
            return processing with { Value = SavedEnum<MapPostProcessingType>(processingName.Value!) };

        if (prototype is SimpleDataComponent<ImmutableArray<ChatMessage>> lore && tag is NbtList lines)
            return lore with { Value = [.. lines.Select(TextFromNbt).OfType<ChatMessage>()] };

        if (prototype is SimpleDataComponent<ImmutableArray<string>> strings && tag is NbtList values)
            return strings with { Value = [.. values.OfType<NbtTag<string>>().Select(value => value.Value!)] };

        if (prototype is SimpleDataComponent<ImmutableArray<ItemStack>> stacks && tag is NbtList items)
            return stacks with { Value = [.. items.OfType<NbtCompound>().Select(ItemFromNbt).OfType<ItemStack>()] };

        if (prototype is SimpleDataComponent<ItemStack?> stack && tag is NbtCompound item)
            return stack with { Value = item.ItemFromNbt() };

        if (prototype is SimpleDataComponent<FireworkExplosion> explosion && tag is NbtCompound firework)
            return explosion with { Value = ExplosionFromNbt(firework) };

        if (prototype is SimpleDataComponent<IdSet> repairable && tag is NbtCompound repair)
            return repairable with { Value = SetFromNbt(repair[type == DataComponentType.DamageResistant ? "types" : "items"],
                type == DataComponentType.DamageResistant ? "minecraft:damage_type" : ComponentValueCodecs.Items) };

        if (prototype is SimpleDataComponent<ImmutableArray<IConsumeEffect>> protection && tag is NbtCompound death)
            return protection with { Value = [.. Compounds(death, "death_effects").Select(ConsumeEffectFromNbt)] };

        if (prototype is SimpleDataComponent<ImmutableArray<BlockStateProperty>> state
            && tag is NbtCompound properties)
        {
            return state with
            {
                Value = [.. properties.Select(pair => new BlockStateProperty
                {
                    Name = pair.Key,
                    Value = ((NbtTag<string>)pair.Value).Value!
                })]
            };
        }

        if (prototype is SimpleDataComponent<ImmutableArray<Page>> writable && tag is NbtCompound pages)
        {
            return writable with
            {
                Value = pages.TryGetTag<NbtList>("pages", out var savedPages)
                    ? [.. savedPages.Select(page => page is NbtCompound wrapper
                        ? new Page
                        {
                            RawContent = wrapper.SavedString("raw")!,
                            FilteredContent = wrapper.SavedString("filtered")
                        }
                        : new Page { RawContent = ((NbtTag<string>)page).Value! })]
                    : []
            };
        }

        if (tag is NbtCompound valueCompound)
            return CompoundComponentFromNbt(type, valueCompound);

        return type switch
        {
            DataComponentType.AttributeModifiers or DataComponentType.BannerPatterns
                or DataComponentType.CanBreak or DataComponentType.CanPlaceOn or DataComponentType.Bees =>
                ListComponentFromNbt(type, tag),
            DataComponentType.Profile when tag is NbtTag<string> username =>
                new ProfileDataComponent { Username = username.Value },
            DataComponentType.JukeboxPlayable => SongFromNbt(tag),
            DataComponentType.Instrument => InstrumentFromNbt(tag),
            _ => null
        };
    }

    private static NbtList SavedList<T>(string name, IEnumerable<T> tags) where T : INbtTag
    {
        var values = tags.Cast<INbtTag>().ToArray();
        var list = new NbtList(values.Length == 0 ? NbtTagType.Compound : values[0].Type, name);
        foreach (var value in values)
            list.Add(value);

        return list;
    }

    private static NbtList SavedStrings(string name, IEnumerable<string> values) =>
        SavedList(name, values.Select(value => new NbtTag<string>(string.Empty, value)));

    private static IEnumerable<NbtCompound> Compounds(NbtCompound tag, string name) =>
        tag.TryGetTag<NbtList>(name, out var list) ? list.OfType<NbtCompound>() : [];

    private static bool ReadBoolean(INbtTag tag) => tag switch
    {
        NbtTag<bool> flag => flag.Value,
        NbtTag<byte> flag => flag.Value != 0,
        _ => throw new InvalidDataException("Expected a saved boolean.")
    };

    private static bool SavedBool(NbtCompound tag, string name, bool fallback = false) =>
        tag.TryGetBool(name, out var value) ? value : fallback;

    private static float SavedFloat(NbtCompound tag, string name, float fallback = 0) =>
        tag.TryGetTagValue<float>(name, out var value) ? value : fallback;

    private static int SavedInt(NbtCompound tag, string name, int fallback = 0) =>
        tag.TryGetTagValue<int>(name, out var value) ? value : fallback;

    private static string? SavedString(this NbtCompound tag, string name) =>
        tag.TryGetTagValue<string>(name, out var value) ? value : null;

    private static T SavedEnum<T>(string name) where T : struct, Enum =>
        Enum.Parse<T>(name[(name.IndexOf(':') + 1)..].Replace("_", string.Empty), true);

    private static NbtCompound StateToNbt(string name, ImmutableArray<BlockStateProperty> properties)
    {
        var tag = new NbtCompound(name);
        foreach (var property in properties)
            tag.Add(new NbtTag<string>(property.Name, property.Value));

        return tag;
    }

    private static NbtCompound FilteredString(string raw, string? filtered, string name = "")
    {
        var tag = new NbtCompound(name) { new NbtTag<string>("raw", raw) };
        if (filtered is not null)
            tag.Add(new NbtTag<string>("filtered", filtered));

        return tag;
    }

    private static NbtCompound FilteredText(ChatMessage raw, ChatMessage? filtered)
    {
        var tag = new NbtCompound { raw.ToNbt("raw") };
        if (filtered is not null)
            tag.Add(filtered.ToNbt("filtered"));

        return tag;
    }

    private static NbtCompound CooldownToNbt(string name, UseCooldownDataComponent cooldown)
    {
        var tag = new NbtCompound(name) { new NbtTag<float>("seconds", cooldown.Seconds) };
        if (cooldown.CooldownGroup is not null)
            tag.Add(new NbtTag<string>("cooldown_group", cooldown.CooldownGroup));

        return tag;
    }

    private static INbtTag SetToNbt(string name, IdSet set, string registry) => set.Type == 0
        ? new NbtTag<string>(name, "#" + set.TagName)
        : SavedStrings(name, (set.Ids ?? []).Select(id => ComponentRegistryNames.Name(registry, id)));

    private static IdSet SetFromNbt(INbtTag tag, string registry)
    {
        if (tag is NbtTag<string> value && value.Value!.StartsWith('#'))
        {
            return new IdSet
            {
                Type = 0,
                TagName = value.Value[1..]
            };
        }

        var names = tag is NbtTag<string> single ? [single.Value!]
            : ((NbtList)tag).OfType<NbtTag<string>>().Select(value => value.Value!);
        var ids = names.Select(name => ComponentRegistryNames.Id(registry, name)).ToImmutableArray();
        return new IdSet
        {
            Type = ids.Length + 1,
            Ids = ids
        };
    }

    private static NbtCompound ToolToNbt(string name, ToolDataComponent tool) => new(name)
    {
        SavedList("rules", tool.Rules.Select(rule =>
        {
            var tag = new NbtCompound { SetToNbt("blocks", rule.Blocks, ComponentValueCodecs.Blocks) };
            if (rule.Speed is float speed)
                tag.Add(new NbtTag<float>("speed", speed));

            if (rule.CorrectDropForBlocks is bool correct)
                tag.Add(new NbtTag<bool>("correct_for_drops", correct));

            return tag;
        })),
        new NbtTag<float>("default_mining_speed", tool.DefaultMiningSpeed),
        new NbtTag<int>("damage_per_block", tool.DamagePerBlock),
        new NbtTag<bool>("can_destroy_blocks_in_creative", tool.CanDestroyBlocksInCreative)
    };

    private static NbtCompound EquipmentToNbt(string name, EquippableDataComponent equipment)
    {
        var tag = new NbtCompound(name)
        {
            new NbtTag<string>("slot", equipment.Slot switch
            {
                EquipmentSlot.Boots => "feet",
                EquipmentSlot.Leggings => "legs",
                EquipmentSlot.Chestplate => "chest",
                EquipmentSlot.Helmet => "head",
                _ => equipment.Slot.ToString().ToSnakeCase().Replace("_hand", "hand")
            }),
            SoundToNbt("equip_sound", equipment.EquipSound),
            new NbtTag<bool>("dispensable", equipment.Dispensable),
            new NbtTag<bool>("swappable", equipment.Swappable),
            new NbtTag<bool>("damage_on_hurt", equipment.DamageOnHurt),
            new NbtTag<bool>("equip_on_interact", equipment.EquipOnInteract),
            new NbtTag<bool>("can_be_sheared", equipment.CanBeSheared),
            SoundToNbt("shearing_sound", equipment.ShearingSound)
        };
        var asset = equipment.AssetIdentifier
            ?? (equipment.AssetId is EquipmentAssets id ? EnumId(id) : null);
        if (asset is not null)
            tag.Add(new NbtTag<string>("asset_id", asset));

        if (equipment.CameraOverlay is not null)
            tag.Add(new NbtTag<string>("camera_overlay", equipment.CameraOverlay));

        var allowed = equipment.AllowedEntitySet
            ?? (equipment.AllowedEntities is ImmutableArray<int> entities
                ? new IdSet
                {
                    Type = 1,
                    Ids = entities
                }
                : null);
        if (allowed is IdSet set)
            tag.Add(SetToNbt("allowed_entities", set, ComponentValueCodecs.EntityTypes));

        return tag;
    }

    private static INbtTag SoundToNbt(string name, SoundEvent sound)
    {
        if (sound.RegistryId is int id)
            return new NbtTag<string>(name, ComponentRegistryNames.Name(ComponentValueCodecs.SoundEvents, id));

        var tag = new NbtCompound(name) { new NbtTag<string>("sound_id", sound.ResourceLocation) };
        if (sound.FixedRange is float range)
            tag.Add(new NbtTag<float>("range", range));

        return tag;
    }

    private static SoundEvent SoundFromNbt(INbtTag tag) => tag is NbtTag<string> name
        ? new SoundEvent
        {
            RegistryId = ComponentRegistryNames.Id(ComponentValueCodecs.SoundEvents, name.Value!),
            ResourceLocation = name.Value!
        }
        : new SoundEvent
        {
            ResourceLocation = ((NbtCompound)tag).SavedString("sound_id")!,
            FixedRange = ((NbtCompound)tag).TryGetTagValue<float>("range", out var range) ? range : null
        };

    private static NbtCompound EntityDataToNbt(string name, EntityDataComponent entity)
    {
        var tag = CopyCompound(name, entity.Data);
        var registry = entity.Type == DataComponentType.BlockEntityData
            ? ComponentValueCodecs.BlockEntityTypes : ComponentValueCodecs.EntityTypes;
        tag.Remove("id");
        tag.Add(new NbtTag<string>("id", ComponentRegistryNames.Name(registry, entity.RegistryId)));
        return tag;
    }

    private static EntityDataComponent EntityDataFromNbt(DataComponentType type, NbtCompound tag)
    {
        var data = CopyCompound(string.Empty, tag);
        var id = data.SavedString("id")!;
        data.Remove("id");
        return new EntityDataComponent(type)
        {
            RegistryId = ComponentRegistryNames.Id(type == DataComponentType.BlockEntityData
                ? ComponentValueCodecs.BlockEntityTypes : ComponentValueCodecs.EntityTypes, id),
            Data = data
        };
    }

    private static NbtCompound LodestoneToNbt(string name, LodestoneTrackerDataComponent tracker)
    {
        var tag = new NbtCompound(name) { new NbtTag<bool>("tracked", tracker.Tracked) };
        if (tracker.Target is GlobalPos target)
        {
            tag.Add(new NbtCompound("target")
            {
                new NbtTag<string>("dimension", target.Dimension),
                new NbtArray<int>("pos", [(int)target.Pos.X, (int)target.Pos.Y, (int)target.Pos.Z])
            });
        }

        return tag;
    }

    private static NbtCompound MapDecorationsToNbt(string name, MapDecorationDataComponent decorations)
    {
        var tag = new NbtCompound(name);
        foreach (var (key, value) in decorations.Decorations)
            tag.Add(value.ToNbt(key));

        return tag;
    }

    private static NbtCompound ExplosionToNbt(string name, FireworkExplosion explosion) => new(name)
    {
        new NbtTag<string>("shape", ((FireworkExplosionShape)explosion.Shape).ToString().ToSnakeCase()),
        new NbtArray<int>("colors", explosion.Colors.ToArray()),
        new NbtArray<int>("fade_colors", explosion.FadeColors.ToArray()),
        new NbtTag<bool>("has_trail", explosion.HasTrail),
        new NbtTag<bool>("has_twinkle", explosion.HasTwinkle)
    };

    private static FireworkExplosion ExplosionFromNbt(NbtCompound tag) => new()
    {
        Shape = tag.SavedString("shape") is string shape ? (int)SavedEnum<FireworkExplosionShape>(shape) : 0,
        Colors = tag.TryGetTag<NbtArray<int>>("colors", out var colors) ? [.. colors.GetArray()] : [],
        FadeColors = tag.TryGetTag<NbtArray<int>>("fade_colors", out var fade) ? [.. fade.GetArray()] : [],
        HasTrail = SavedBool(tag, "has_trail"),
        HasTwinkle = SavedBool(tag, "has_twinkle")
    };

    private static DataComponent? CompoundComponentFromNbt(DataComponentType type, NbtCompound tag) => type switch
    {
        DataComponentType.Food => new FoodDataComponent
        {
            Nutrition = tag.GetInt("nutrition"),
            SaturationModifier = tag.GetFloat("saturation"),
            CanAlwaysEat = SavedBool(tag, "can_always_eat")
        },
        DataComponentType.UseCooldown => new UseCooldownDataComponent
        {
            Seconds = tag.GetFloat("seconds"),
            CooldownGroup = tag.SavedString("cooldown_group")
        },
        DataComponentType.Tool => new ToolDataComponent
        {
            Rules = [.. Compounds(tag, "rules").Select(rule => new ToolRule
            {
                Blocks = SetFromNbt(rule["blocks"], ComponentValueCodecs.Blocks),
                Speed = rule.TryGetTagValue<float>("speed", out var speed) ? speed : null,
                CorrectDropForBlocks = rule.TryGetBool("correct_for_drops", out var correct) ? correct : null
            })],
            DefaultMiningSpeed = SavedFloat(tag, "default_mining_speed", 1),
            DamagePerBlock = SavedInt(tag, "damage_per_block", 1),
            CanDestroyBlocksInCreative = SavedBool(tag, "can_destroy_blocks_in_creative", true)
        },
        DataComponentType.Consumable => new ConsumableDataComponent
        {
            ConsumeSeconds = SavedFloat(tag, "consume_seconds", 1.6f),
            Animation = SavedEnum<ItemAnimation>(tag.SavedString("animation") ?? "eat"),
            Sound = tag.TryGetTag("sound", out var sound) ? SoundFromNbt(sound) : ComponentBuilder.Consumable.Sound,
            HasConsumeParticles = SavedBool(tag, "has_consume_particles", true),
            Effects = [.. Compounds(tag, "on_consume_effects").Select(effect => new ConsumeEffect
            {
                Type = effect.SavedString("type")!,
                Effect = ConsumeEffectFromNbt(effect)
            })]
        },
        DataComponentType.Equippable => EquipmentFromNbt(tag),
        DataComponentType.EntityData or DataComponentType.BlockEntityData => EntityDataFromNbt(type, tag),
        DataComponentType.CustomModelData => new CustomModelDataComponent
        {
            Floats = [.. ListValues<float>(tag, "floats")],
            Flags = tag.TryGetTag<NbtList>("flags", out var flags) ? [.. flags.Select(ReadBoolean)] : [],
            Strings = [.. ListValues<string>(tag, "strings")],
            Colors = [.. ListValues<int>(tag, "colors")]
        },
        DataComponentType.LodestoneTracker => new LodestoneTrackerDataComponent
        {
            Tracked = SavedBool(tag, "tracked", true),
            Target = tag.TryGetTag<NbtCompound>("target", out var target)
                && target.TryGetTag<NbtArray<int>>("pos", out var position)
                ? new GlobalPos
                {
                    Dimension = target.SavedString("dimension")!,
                    Pos = new Vector(position[0], position[1], position[2])
                }
                : null
        },
        DataComponentType.MapDecorations => new MapDecorationDataComponent
        {
            Decorations = tag.ToDictionary(pair => pair.Key, pair => DecorationFromNbt((NbtCompound)pair.Value))
        },
        DataComponentType.WrittenBookContent => new WrittenBookDataComponent
        {
            Title = tag["title"] is NbtCompound title
                ? title.SavedString("raw")! : ((NbtTag<string>)tag["title"]).Value!,
            FilteredTitle = tag["title"] is NbtCompound filteredTitle ? filteredTitle.SavedString("filtered") : null,
            Author = tag.SavedString("author")!,
            Generation = SavedInt(tag, "generation"),
            Pages = tag.TryGetTag<NbtList>("pages", out var pages)
                ? [.. pages.Select(page => page is NbtCompound wrapper && wrapper.HasTag("raw")
                    ? (wrapper["raw"].TextFromNbt()!, wrapper.TryGetTag("filtered", out var filtered)
                        ? filtered.TextFromNbt() : null)
                    : (page.TextFromNbt()!, (ChatMessage?)null))]
                : [],
            Resolved = SavedBool(tag, "resolved")
        },
        DataComponentType.Trim => TrimFromNbt(tag),
        DataComponentType.Profile => ProfileFromNbt(tag),
        DataComponentType.JukeboxPlayable => SongFromNbt(tag),
        DataComponentType.Instrument => InstrumentFromNbt(tag),
        DataComponentType.CanBreak or DataComponentType.CanPlaceOn => ListComponentFromNbt(type, tag),
        _ => null
    };

    private static IEnumerable<T> ListValues<T>(NbtCompound tag, string name) =>
        tag.TryGetTag<NbtList>(name, out var list) ? list.OfType<NbtTag<T>>().Select(value => value.Value!) : [];

    private static MapDecoration DecorationFromNbt(NbtCompound tag) => new()
    {
        Type = SavedEnum<MapDecorationType>(tag.SavedString("type")!),
        X = tag.GetDouble("x"),
        Z = tag.GetDouble("z"),
        Rotation = tag.GetFloat("rotation")
    };

    private static EquippableDataComponent EquipmentFromNbt(NbtCompound tag) => ComponentBuilder.Equippable with
    {
        Slot = tag.SavedString("slot") switch
        {
            "feet" => EquipmentSlot.Boots,
            "legs" => EquipmentSlot.Leggings,
            "chest" => EquipmentSlot.Chestplate,
            "head" => EquipmentSlot.Helmet,
            var slot => SavedEnum<EquipmentSlot>(slot!)
        },
        EquipSound = tag.TryGetTag("equip_sound", out var sound)
            ? SoundFromNbt(sound) : ComponentBuilder.Equippable.EquipSound,
        AssetIdentifier = tag.SavedString("asset_id"),
        CameraOverlay = tag.SavedString("camera_overlay"),
        AllowedEntitySet = tag.TryGetTag("allowed_entities", out var entities)
            ? SetFromNbt(entities, ComponentValueCodecs.EntityTypes) : null,
        Dispensable = SavedBool(tag, "dispensable", true),
        Swappable = SavedBool(tag, "swappable", true),
        DamageOnHurt = SavedBool(tag, "damage_on_hurt", true),
        EquipOnInteract = SavedBool(tag, "equip_on_interact"),
        CanBeSheared = SavedBool(tag, "can_be_sheared"),
        ShearingSound = tag.TryGetTag("shearing_sound", out var shearing)
            ? SoundFromNbt(shearing) : ComponentBuilder.Equippable.ShearingSound
    };
}
