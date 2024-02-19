using Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Chat;
using Obsidian.API.Registry.Codecs.DamageTypes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.Registries;
using System.Text.Json;

namespace Obsidian.Utilities;

//TODO MAKE NBT DE/SERIALIZERS PLEASE
public partial class Extensions
{
    public static NbtCompound ToNbt(this ChatMessage chatMessage, string name = "")
    {
        var compound = new NbtCompound(name)
        {
            new NbtTag<bool>("bold", chatMessage.Bold),
            new NbtTag<bool>("italic", chatMessage.Italic),
            new NbtTag<bool>("underlined", chatMessage.Underlined),
            new NbtTag<bool>("strikethrough", chatMessage.Strikethrough),
            new NbtTag<bool>("obfuscated", chatMessage.Obfuscated)
        };

        if (!chatMessage.Text.IsNullOrEmpty())
            compound.Add(new NbtTag<string>("text", chatMessage.Text!));
        if (!chatMessage.Translate.IsNullOrEmpty())
            compound.Add(new NbtTag<string>("translate", chatMessage.Translate!));
        if (chatMessage.Color.HasValue)
            compound.Add(new NbtTag<string>("color", chatMessage.Color.Value.ToString()));
        if (!chatMessage.Insertion.IsNullOrEmpty())
            compound.Add(new NbtTag<string>("insertion", chatMessage.Insertion!));

        if (chatMessage.ClickEvent != null)
            compound.Add(chatMessage.ClickEvent.ToNbt());
        if (chatMessage.HoverEvent != null)
            compound.Add(chatMessage.HoverEvent.ToNbt());

        return compound;
    }

    public static NbtCompound ToNbt(this HoverComponent hoverComponent)
    {
        var compound = new NbtCompound("hoverEvent")
        {
            new NbtTag<string>("action", JsonNamingPolicy.SnakeCaseLower.ConvertName(hoverComponent.Action.ToString())),
        };


        if (hoverComponent.Contents is HoverChatContent chatContent)
            compound.Add(chatContent.ChatMessage.ToNbt("contents"));
        else if (hoverComponent.Contents is HoverItemContent)
            throw new NotImplementedException("Missing properties from ItemStack can't implement.");
        else if (hoverComponent.Contents is HoverEntityComponent entityComponent)
        {
            var entityCompound = new NbtCompound("contents")
            {
                new NbtTag<string>("id", entityComponent.Entity.Uuid.ToString()),
            };

            if (entityComponent.Entity.CustomName is ChatMessage name)
                entityCompound.Add(name.ToNbt("name"));
            else
                entityCompound.Add(new NbtTag<string>("name", entityComponent.Entity.Type.ToString()));
        }

        return compound;
    }

    public static NbtCompound ToNbt(this ClickComponent clickComponent) => new("clickEvent")
    {
         new NbtTag<string>("action", JsonNamingPolicy.SnakeCaseLower.ConvertName(clickComponent.Action.ToString())),
         new NbtTag<string>("value", clickComponent.Value)
    };


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

        var itemStack = ItemsRegistry.GetSingleItem(item.GetString("id"));

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
        INbtTag monsterSpawnLightLevel = default!;

        if (value.Element.MonsterSpawnLightLevel is MonsterLightLevelUniformValue uniformValue)
        {
            //monsterSpawnLightLevel = new NbtCompound("monster_spawn_light_level")
            //{
            //    new NbtTag<int>("min_inclusive", value.Element.MonsterSpawnLightLevel.Value?.MinInclusive ?? 0),
            //    new NbtTag<int>("max_inclusive", value.Element.MonsterSpawnLightLevel.Value?.MaxInclusive ?? 0),
            //    new NbtTag<string>("type", value.Element.MonsterSpawnLightLevel.Value?.Type ?? string.Empty)
            //};
            monsterSpawnLightLevel = new NbtTag<int>("monster_spawn_light_level", uniformValue.MaxInclusive);
        }
        else if(value.Element.MonsterSpawnLightLevel is MonsterSpawnLightLevelIntValue intValue)
            monsterSpawnLightLevel = new NbtTag<int>("monster_spawn_light_level", intValue.Value);

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


    #region Damage Type Codec Writing
    public static void Write(this DamageTypeCodec value, NbtList list)
    {
        var compound = new NbtCompound
        {
            new NbtTag<int>("id", value.Id),

            new NbtTag<string>("name", value.Name),

            value.WriteElement()
        };

        list.Add(compound);
    }

    public static NbtCompound WriteElement(this DamageTypeCodec value)
    {
        var damageTypeElement = value.Element;

        var element = new NbtCompound("element")
        {
            new NbtTag<float>("exhaustion", damageTypeElement.Exhaustion),
            new NbtTag<string>("message_id", damageTypeElement.MessageId),
            new NbtTag<string>("scaling", damageTypeElement.Scaling.ToString().ToSnakeCase())
        };

        if (damageTypeElement.DeathMessageType is DeathMessageType deathMessageType)
            element.Add(new NbtTag<string>("death_message_type", deathMessageType.ToString().ToSnakeCase()));
        if (damageTypeElement.Effects is DamageEffects damageEffects)
            element.Add(new NbtTag<string>("effects", damageEffects.ToString().ToSnakeCase()));

        return element;
    }
    #endregion

    #region Chat Codec Writing
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
        var chat = chatElement.Chat;
        var narration = chatElement.Narration;

        var chatParameters = new NbtList(NbtTagType.String, "parameters");
        var narrationParameters = new NbtList(NbtTagType.String, "parameters");

        foreach (var param in chat.Parameters)
            chatParameters.Add(new NbtTag<string>("", param));
        foreach (var param in narration.Parameters)
            narrationParameters.Add(new NbtTag<string>("", param));

        var chatCompound = new NbtCompound("chat")
        {
            chatParameters,
            new NbtTag<string>("translation_key", chat.TranslationKey)
        };

        if (chat.Style is ChatStyle style)
        {
            chatCompound.Add(new NbtCompound("style")
            {
                new NbtTag<string>("color", style.Color),
                new NbtTag<bool>("italic", style.Italic)
            });
        }

        var narrationCompound = new NbtCompound("narration")
        {
            narrationParameters,
            new NbtTag<string>("translation_key", narration.TranslationKey)
        };

        var element = new NbtCompound("element")
        {
            chatCompound,
            narrationCompound,
        };

        return element;
    }
    #endregion

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
            new NbtTag<bool>("has_precipitation", value.Element.HasPrecipitation),
            new NbtTag<float>("depth", value.Element.Depth),
            new NbtTag<float>("temperature", value.Element.Temperature),
            new NbtTag<float>("scale", value.Element.Scale),
            new NbtTag<float>("downfall", value.Element.Downfall)
        };

        if (!value.Element.Category.IsNullOrEmpty())
            elements.Add(new NbtTag<string>("category", value.Element.Category!));

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

    #region Trim Pattern Writing 
    public static void Write(this TrimPatternCodec value, NbtList list)
    {
        var compound = new NbtCompound
        {
            new NbtTag<int>("id", value.Id),

            new NbtTag<string>("name", value.Name),

            value.WriteElement()
        };

        list.Add(compound);
    }

    public static NbtCompound WriteElement(this TrimPatternCodec value)
    {
        var patternElement = value.Element;

        var description = new NbtList(NbtTagType.String, "description")
        {
            new NbtTag<string>("translate", patternElement.Description.Translate)
        };

        var element = new NbtCompound("element")
        {
            new NbtTag<string>("template_item", patternElement.TemplateItem),
            description,
            new NbtTag<string>("asset_id", patternElement.AssetId),
            new NbtTag<bool>("decal", patternElement.Decal)
        };

        return element;
    }
    #endregion

    #region Trim Material Writing
    public static void Write(this TrimMaterialCodec value, NbtList list)
    {
        var compound = new NbtCompound
        {
            new NbtTag<int>("id", value.Id),

            new NbtTag<string>("name", value.Name),

            value.WriteElement()
        };

        list.Add(compound);
    }

    public static NbtCompound WriteElement(this TrimMaterialCodec value)
    {
        var materialElement = value.Element;

        var description = new NbtList(NbtTagType.String, "description")
        {
            new NbtTag<string>("translate", materialElement.Description.Translate),
            new NbtTag<string>("color", materialElement.Description.Color!)
        };

        var element = new NbtCompound("element")
        {
            new NbtTag<string>("ingredient", materialElement.Ingredient),
            description,
            new NbtTag<string>("asset_name", materialElement.AssetName),
            new NbtTag<double>("item_model_index", materialElement.ItemModelIndex)
        };

        if (materialElement.OverrideArmorMaterials is Dictionary<string, string> overrideArmorMats)
        {
            var overrideArmorMaterialsCompound = new NbtCompound("override_armor_materials");

            foreach (var (type, replacement) in overrideArmorMats)
                overrideArmorMaterialsCompound.Add(new NbtTag<string>(type, replacement));

            element.Add(overrideArmorMaterialsCompound);
        }

        return element;
    }
    #endregion
}
