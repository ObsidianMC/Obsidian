using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Chat;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Nbt;

namespace Obsidian.Utilities;

//TODO MAKE NBT DE/SERIALIZERS PLEASE
public partial class Extensions
{
    public static NbtCompound ToNbt(this ItemStack? value)
    {
        value ??= new ItemStack(0, 0) { Present = true };

        var item = value.AsItem();

        var compound = new NbtCompound
            {
                new NbtTag<string>("id", item.UnlocalizedName),
                new NbtTag<byte>("Count", (byte)value.Count),
                new NbtTag<byte>("Slot", (byte)value.Slot)
            };

        ItemMeta meta = value.ItemMeta;

        if (meta.HasTags())
        {
            compound.Add(new NbtTag<bool>("Unbreakable", meta.Unbreakable));

            if (meta.Durability > 0)
                compound.Add(new NbtTag<int>("Damage", meta.Durability));

            if (meta.CustomModelData > 0)
                compound.Add(new NbtTag<int>("CustomModelData", meta.CustomModelData));

            if (meta.CanDestroy is not null)
            {
                var list = new NbtList(NbtTagType.String, "CanDestroy");

                foreach (var block in meta.CanDestroy)
                    list.Add(new NbtTag<string>(string.Empty, block));

                compound.Add(list);
            }

            if (meta.Name is not null)
            {
                var displayCompound = new NbtCompound("display")
                {
                    new NbtTag<string>("Name", new List<ChatMessage> { meta.Name }.ToJson())
                };

                if (meta.Lore is not null)
                {
                    var list = new NbtList(NbtTagType.String, "Lore");

                    foreach (var lore in meta.Lore)
                        list.Add(new NbtTag<string>(string.Empty, new List<ChatMessage> { lore }.ToJson()));

                    displayCompound.Add(list);
                }

                compound.Add(displayCompound);
            }
            else if (meta.Lore is not null)
            {
                var displayCompound = new NbtCompound("display")
                {
                    new NbtTag<string>("Name", new List<ChatMessage> { meta.Name }.ToJson())
                };

                var list = new NbtList(NbtTagType.String, "Lore");

                foreach (var lore in meta.Lore)
                    list.Add(new NbtTag<string>(string.Empty, new List<ChatMessage> { lore }.ToJson()));

                displayCompound.Add(list);

                compound.Add(displayCompound);
            }
        }

        return compound;
    }

    public static ItemStack? ItemFromNbt(this NbtCompound? item)
    {
        if (item is null)
            return null;

        var itemStack = Registry.Registry.GetSingleItem(item.GetString("id"));

        var itemMetaBuilder = new ItemMetaBuilder();

        foreach (var (name, child) in item)
        {
            switch (name.ToUpperInvariant())
            {
                case "ENCHANTMENTS":
                    {
                        var enchantments = (NbtList)child;

                        foreach (var enchant in enchantments)
                        {
                            if (enchant is NbtCompound compound)
                            {
                                itemMetaBuilder.AddEnchantment(compound.GetString("id").ToEnchantType(), compound.GetShort("lvl"));
                            }
                        }

                        break;
                    }

                case "STOREDENCHANTMENTS":
                    {
                        var enchantments = (NbtList)child;

                        foreach (var enchantment in enchantments)
                        {
                            if (enchantment is NbtCompound compound)
                            {
                                compound.TryGetTag("id", out var id);
                                compound.TryGetTag("lvl", out var lvl);

                                itemMetaBuilder.AddStoredEnchantment(compound.GetString("id").ToEnchantType(), compound.GetShort("lvl"));
                            }
                        }
                        break;
                    }

                case "SLOT":
                    {
                        var byteTag = (NbtTag<byte>)child;

                        itemStack.Slot = byteTag.Value;
                        break;
                    }

                case "DAMAGE":
                    {
                        var intTag = (NbtTag<int>)child;

                        itemMetaBuilder.WithDurability(intTag.Value);
                        break;
                    }

                case "DISPLAY":
                    {
                        var display = (NbtCompound)child;

                        foreach (var (displayTagName, displayTag) in display)
                        {
                            if (displayTagName.EqualsIgnoreCase("name") && displayTag is NbtTag<string> stringTag)
                            {
                                itemMetaBuilder.WithName(stringTag.Value);
                            }
                            else if (displayTag.Name.EqualsIgnoreCase("lore"))
                            {
                                var loreTag = (NbtList)displayTag;

                                foreach (NbtTag<string> lore in loreTag)
                                    itemMetaBuilder.AddLore(lore.Value.FromJson<ChatMessage>());
                            }
                        }
                        break;
                    }
            }
        }

        itemStack.ItemMeta = itemMetaBuilder.Build();

        return itemStack;
    }

    #region Dimension Codec Writing
    public static NbtCompound WriteElement(this DimensionCodec value)
    {
        INbtTag monsterSpawnLightLevel;

        if (value.Element.MonsterSpawnLightLevel.Value.HasValue)
        {
            monsterSpawnLightLevel = new NbtCompound("monster_spawn_light_level")
            {
                new NbtTag<int>("min_inclusive", value.Element.MonsterSpawnLightLevel.Value?.MinInclusive ?? 0),
                new NbtTag<int>("max_inclusive", value.Element.MonsterSpawnLightLevel.Value?.MaxInclusive ?? 0),
                new NbtTag<string>("type", value.Element.MonsterSpawnLightLevel.Value?.Type ?? string.Empty)
            };
        }
        else
            monsterSpawnLightLevel = new NbtTag<int>("monster_spawn_light_level", value.Element.MonsterSpawnLightLevel.IntValue ?? 0);

        var compound = new NbtCompound("element")
        {
            new NbtTag<bool>("piglin_safe", value.Element.PiglinSafe),

            new NbtTag<bool>("natural", value.Element.Natural),

            new NbtTag<float>("ambient_light", value.Element.AmbientLight),

            new NbtTag<string>("infiniburn", value.Element.Infiniburn),

            new NbtTag<bool>("respawn_anchor_works", value.Element.RespawnAnchorWorks),
            new NbtTag<bool>("has_skylight", value.Element.HasSkylight),
            new NbtTag<bool>("bed_works", value.Element.BedWorks),

            new NbtTag<string>("effects", value.Element.Effects),

            new NbtTag<bool>("has_raids", value.Element.HasRaids),

            new NbtTag<int>("min_y", value.Element.MinY),
            new NbtTag<int>("height", value.Element.Height),
            new NbtTag<int>("logical_height", value.Element.LogicalHeight),

            new NbtTag<double>("coordinate_scale", value.Element.CoordinateScale),

            new NbtTag<bool>("ultrawarm", value.Element.Ultrawarm),
            new NbtTag<bool>("has_ceiling", value.Element.HasCeiling),

            new NbtTag<int>("monster_spawn_block_light_limit", value.Element.MonsterSpawnBlockLightLimit),

            monsterSpawnLightLevel
        };

        if (value.Element.FixedTime.HasValue)
            compound.Add(new NbtTag<long>("fixed_time", value.Element.FixedTime.Value));

        return compound;
    }

    public static void Write(this DimensionCodec value, NbtList list)
    {
        var compound = new NbtCompound
        {
            new NbtTag<int>("id", value.Id),

            new NbtTag<string>("name", value.Name),

            value.WriteElement()
        };

        list.Add(compound);
    }

    public static void WriteElement(this DimensionCodec value, NbtWriter writer)
    {
        writer.WriteBool("piglin_safe", value.Element.PiglinSafe);
        writer.WriteBool("natural", value.Element.Natural);

        writer.WriteFloat("ambient_light", value.Element.AmbientLight);

        if (value.Element.FixedTime.HasValue)
            writer.WriteLong("fixed_time", value.Element.FixedTime.Value);

        writer.WriteString("infiniburn", value.Element.Infiniburn);

        writer.WriteBool("respawn_anchor_works", value.Element.RespawnAnchorWorks);
        writer.WriteBool("has_skylight", value.Element.HasSkylight);
        writer.WriteBool("bed_works", value.Element.BedWorks);

        writer.WriteString("effects", value.Element.Effects);

        writer.WriteBool("has_raids", value.Element.HasRaids);

        writer.WriteInt("min_y", value.Element.MinY);

        writer.WriteInt("height", value.Element.Height);

        writer.WriteInt("logical_height", value.Element.LogicalHeight);

        writer.WriteFloat("coordinate_scale", value.Element.CoordinateScale);

        writer.WriteBool("ultrawarm", value.Element.Ultrawarm);
        writer.WriteBool("has_ceiling", value.Element.HasCeiling);
    }
    #endregion

    public static void Write(this ChatCodec value, NbtList list)
    {
        var compound = new NbtCompound
        {
            new NbtTag<int>("id", value.Id),

            new NbtTag<string>("name", value.Name),

            value.WriteElement()
        };

        list.Add(compound);
    }

    public static NbtCompound WriteElement(this ChatCodec value)
    {
        var chatElement = value.Element;
        var chatList = new NbtList(NbtTagType.String, "parameters");

        if(chatElement.Chat?.Decoration?.Parameters != null)
        {
            foreach (var parameter in chatElement.Chat?.Decoration?.Parameters)
                chatList.Add(new NbtTag<string>(string.Empty, parameter));
        }

        var narrationList = new NbtList(NbtTagType.String, "parameters");
        if (chatElement.Chat?.Decoration?.Parameters != null)
        {
            foreach (var parameter in chatElement.Narration?.Decoration?.Parameters)
                narrationList.Add(new NbtTag<string>(string.Empty, parameter));
        }

        var element = new NbtCompound("element")
        {
            new NbtCompound("chat")
            {
                new NbtTag<string>("priority", chatElement.Chat?.Priority ?? string.Empty),
                new NbtCompound("decoration")
                {
                    chatList,
                    new NbtTag<string>("translation_key", chatElement.Chat?.Decoration?.TranslationKey ?? string.Empty),
                    new NbtCompound("style")
                }
            },
            new NbtCompound("Narration")
            {
                new NbtTag<string>("priority", chatElement.Narration?.Priority ?? string.Empty),
                new NbtCompound("decoration")
                {
                    chatList,
                    new NbtTag<string>("translation_key", chatElement.Narration?.Decoration?.TranslationKey ?? string.Empty),
                    new NbtCompound("style")
                }
            }
        };

        return element;
    }

    #region Biome Codec Writing
    public static void Write(this BiomeCodec value, NbtList list)
    {
        var compound = new NbtCompound
        {
            new NbtTag<string>("name", value.Name),
            new NbtTag<int>("id", value.Id),

            value.WriteElement()
        };

        list.Add(compound);
    }

    public static NbtCompound WriteElement(this BiomeCodec value)
    {
        var elements = new NbtCompound("element")
        {
            new NbtTag<string>("precipitation", value.Element.Precipitation),
            new NbtTag<string>("category", value.Element.Category),

            new NbtTag<float>("depth", value.Element.Depth),
            new NbtTag<float>("temperature", value.Element.Temperature),
            new NbtTag<float>("scale", value.Element.Scale),
            new NbtTag<float>("downfall", value.Element.Downfall)
        };

        value.Element.Effects.WriteEffect(elements);

        if (!value.Element.TemperatureModifier.IsNullOrEmpty())
            elements.Add(new NbtTag<string>("temperature_modifier", value.Element.TemperatureModifier));

        return elements;
    }

    public static void WriteEffect(this BiomeEffect value, NbtCompound compound)
    {
        var effects = new NbtCompound("effects")
        {
            new NbtTag<int>("fog_color", value.FogColor),
            new NbtTag<int>("sky_color", value.SkyColor),
            new NbtTag<int>("water_color", value.WaterColor),
            new NbtTag<int>("water_fog_color", value.WaterFogColor)
        };

        if (value.FoliageColor > 0)
            effects.Add(new NbtTag<int>("foliage_color", value.FoliageColor));

        if (value.GrassColor > 0)
            effects.Add(new NbtTag<int>("grass_color", value.GrassColor));

        if (!value.GrassColorModifier.IsNullOrEmpty())
            effects.Add(new NbtTag<string>("grass_color_modifier", value.GrassColorModifier));

        if (value.AdditionsSound != null)
            value.AdditionsSound.WriteAdditionSound(effects);

        if (value.MoodSound != null)
            value.MoodSound.WriteMoodSound(effects);

        if (value.Music != null)
            value.Music.WriteMusic(effects);

        if (!value.AmbientSound.IsNullOrEmpty())
            effects.Add(new NbtTag<string>("ambient_sound", value.AmbientSound));

        if (value.Particle != null)
            value.Particle.WriteParticle(compound);

        compound.Add(effects);
    }

    public static void WriteMusic(this BiomeMusicEffect musicEffect, NbtCompound compound)
    {
        var music = new NbtCompound("music")
        {
            new NbtTag<bool>("replace_current_music", musicEffect.ReplaceCurrentMusic),
            new NbtTag<string>("sound", musicEffect.Sound),
            new NbtTag<int>("max_delay", musicEffect.MaxDelay),
            new NbtTag<int>("min_delay", musicEffect.MinDelay)
        };

        compound.Add(music);
    }

    public static void WriteAdditionSound(this BiomeAdditionSound value, NbtCompound compound)
    {
        var additions = new NbtCompound("additions_sound")
        {
            new NbtTag<string>("sound", value.Sound),
            new NbtTag<double>("tick_chance", value.TickChance)
        };

        compound.Add(additions);
    }

    public static void WriteMoodSound(this BiomeMoodSound value, NbtCompound compound)
    {
        var mood = new NbtCompound("mood_sound")
        {
            new NbtTag<string>("sound", value.Sound),

            new NbtTag<double>("offset", value.Offset),

            new NbtTag<int>("tick_delay", value.TickDelay),
            new NbtTag<int>("block_search_extent", value.BlockSearchExtent)
        };

        compound.Add(mood);
    }

    public static void WriteParticle(this BiomeParticle value, NbtCompound compound)
    {
        var particle = new NbtCompound("particle")
        {
            new NbtTag<float>("probability", value.Probability)
        };

        if (value.Options != null)
        {
            var options = new NbtCompound("options")
            {
                new NbtTag<string>("type", value.Options.Type)
            };

            particle.Add(options);
        }

        compound.Add(particle);
    }
    #endregion
}
