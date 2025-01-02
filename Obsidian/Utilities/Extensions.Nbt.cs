using Obsidian.API.Inventory;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Chat;
using Obsidian.API.Registry.Codecs.DamageTypes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.API.Registry.Codecs.PaintingVariant;
using Obsidian.API.Registry.Codecs.WolfVariant;
using Obsidian.API.Utilities;
using Obsidian.Nbt;
using System.Reflection;

namespace Obsidian.Utilities;

//TODO MAKE NBT DE/SERIALIZERS PLEASE
public partial class Extensions
{
    //TODO SERIALIZE COMPONENTS TO NBT
    public static NbtCompound ToNbt(this ItemStack? value)
    {
        value ??= ItemStack.Air;

        var item = value.AsItem();

        var compound = new NbtCompound
            {
                new NbtTag<string>("id", item.UnlocalizedName),
                new NbtTag<byte>("Count", (byte)value.Count),
                new NbtTag<byte>("Slot", (byte)value.Slot)
            };

        return compound;
    }


    //DESERIALIZE ITEM COMPONENTS
    public static ItemStack? ItemFromNbt(this NbtCompound? item)
    {
        if (item is null)
            return null;

        var itemStack = ItemsRegistry.GetSingleItem(item.GetString("id"));

        return itemStack;
    }

    //TODO this can be made A LOT FASTER
    public static IBlock ToBlock(this NbtCompound comp)
    {
        var name = comp.GetString("Name").Split(":")[1].ToPascalCase();
        Type builderType = typeof(IBlockState).Assembly.GetType($"Obsidian.API.BlockStates.Builders.{name}StateBuilder");

        if (builderType == null)
        {
            return BlocksRegistry.Get(comp.GetString("Name"));
        }
        var inst = Activator.CreateInstance(builderType);

        if (comp.TryGetTag("Properties", out var props))
        {
            foreach (var prop in props as NbtCompound)
            {
                var instProp = builderType.GetProperty(prop.Key.ToPascalCase());
                Type propType = instProp.PropertyType;
                if (propType.IsSubclassOf(typeof(Enum)))
                {
                    if (prop.Value is NbtTag<string> enumVal && Enum.TryParse(propType, enumVal.Value.ToPascalCase(), out var val))
                        instProp.SetValue(inst, val);
                }
                else if (propType.Name == "Boolean")
                {
                    if (prop.Value is NbtTag<string> boolVal && bool.TryParse(boolVal.Value, out var val))
                        instProp.SetValue(inst, val);
                }
                else if (propType.Name == "Int32")
                {
                    if (prop.Value is NbtTag<string> numVal && int.TryParse(numVal.Value, out var val))
                        instProp.SetValue(inst, val);
                }
            }
        }

        MethodInfo buildMeth = builderType.GetMethod("Build");
        var bs = (IBlockState)buildMeth.Invoke(inst, null);
        var n = comp.GetString("Name");
        return BlocksRegistry.Get(n, bs);
    }

    #region Dimension Codec Writing
    public static NbtCompound WriteElement(this DimensionCodec value)
    {
        INbtTag monsterSpawnLightLevel;

        if (value.Element.MonsterSpawnLightLevel.Value.HasValue)
        {
            //monsterSpawnLightLevel = new NbtCompound("monster_spawn_light_level")
            //{
            //    new NbtTag<int>("min_inclusive", value.Element.MonsterSpawnLightLevel.Value?.MinInclusive ?? 0),
            //    new NbtTag<int>("max_inclusive", value.Element.MonsterSpawnLightLevel.Value?.MaxInclusive ?? 0),
            //    new NbtTag<string>("type", value.Element.MonsterSpawnLightLevel.Value?.Type ?? string.Empty)
            //};
            monsterSpawnLightLevel = new NbtTag<int>("monster_spawn_light_level", value.Element.MonsterSpawnLightLevel.Value?.MaxInclusive ?? 0);
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

        writer.WriteInt("monster_spawn_block_light_limit", value.Element.MonsterSpawnBlockLightLimit);

        if (value.Element.MonsterSpawnLightLevel.IntValue.HasValue)
            writer.WriteInt("monster_spawn_light_level", value.Element.MonsterSpawnLightLevel.IntValue.Value);
        else
        {
            var monsterLight = value.Element.MonsterSpawnLightLevel.Value!.Value;
            writer.WriteTag(new NbtCompound("monster_spawn_light_level")
            {
                new NbtTag<string>("type", monsterLight.Type),
                new NbtTag<int>("max_inclusive", monsterLight.MaxInclusive),
                new NbtTag<int>("min_inclusive", monsterLight.MinInclusive)
            });
        }

        writer.WriteInt("min_y", value.Element.MinY);

        writer.WriteInt("height", value.Element.Height);

        writer.WriteInt("logical_height", value.Element.LogicalHeight);

        writer.WriteFloat("coordinate_scale", value.Element.CoordinateScale);

        writer.WriteBool("ultrawarm", value.Element.Ultrawarm);
        writer.WriteBool("has_ceiling", value.Element.HasCeiling);
    }
    #endregion


    #region Damage Type Codec Writing

    public static void WriteElement(this DamageTypeCodec value, NbtWriter writer)
    {
        var damageTypeElement = value.Element;

        if (damageTypeElement.DeathMessageType is DeathMessageType deathMessageType)
            writer.WriteString("death_message_type", deathMessageType.ToString().ToSnakeCase());
        if (damageTypeElement.Effects is DamageEffects damageEffects)
            writer.WriteString("effects", damageEffects.ToString().ToSnakeCase());

        writer.WriteFloat("exhaustion", damageTypeElement.Exhaustion);
        writer.WriteString("message_id", damageTypeElement.MessageId);
        writer.WriteString("scaling", damageTypeElement.Scaling.ToString().ToSnakeCase());
    }
    #endregion

    #region Chat Codec Writing
    public static void WriteElement(this ChatTypeCodec value, NbtWriter writer)
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

        writer.WriteTag(chatCompound);
        writer.WriteTag(narrationCompound);
    }
    #endregion

    #region Biome Codec Writing
    public static void WriteElement(this BiomeCodec value, NbtWriter writer)
    {
        writer.WriteBool("has_precipitation", value.Element.HasPrecipitation);
        writer.WriteFloat("depth", value.Element.Depth);
        writer.WriteFloat("temperature", value.Element.Temperature);
        writer.WriteFloat("scale", value.Element.Scale);
        writer.WriteFloat("downfall", value.Element.Downfall);

        if (!value.Element.Category.IsNullOrEmpty())
            writer.WriteString("category", value.Element.Category!);

        value.Element.Effects.WriteEffect(writer);

        if (!value.Element.TemperatureModifier.IsNullOrEmpty())
            writer.WriteString("temperature_modifier", value.Element.TemperatureModifier);
    }

    public static void WriteEffect(this BiomeEffect value, NbtWriter writer)
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

        value.AdditionsSound?.WriteAdditionSound(effects);
        value.MoodSound?.WriteMoodSound(effects);
        value.Music?.WriteMusic(effects);

        if (!value.AmbientSound.IsNullOrEmpty())
            effects.Add(new NbtTag<string>("ambient_sound", value.AmbientSound));

        value.Particle?.WriteParticle(writer);

        writer.WriteTag(effects);
    }

    public static void WriteMusic(this BiomeMusicEffectData[] musicEffect, NbtCompound compound)
    {
        var list = new NbtList(NbtTagType.Compound, "music");

        foreach (var musicData in musicEffect)
        {
            var data = musicData.Data;
            var entry = new NbtCompound()
            {
                new NbtCompound("data")
                {
                    new NbtTag<bool>("replace_current_music", data.ReplaceCurrentMusic),
                    new NbtTag<string>("sound", data.Sound),
                    new NbtTag<int>("max_delay", data.MaxDelay),
                    new NbtTag<int>("min_delay", data.MinDelay)
                },
                new NbtTag<int>("weight", musicData.Weight)
            };

            list.Add(entry);
        }

        compound.Add(list);
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

    public static void WriteParticle(this BiomeParticle value, NbtWriter writer)
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

        writer.WriteTag(particle);
    }
    #endregion

    #region Trim Pattern Writing 

    public static void WriteElement(this TrimPatternCodec value, NbtWriter writer)
    {
        var patternElement = value.Element;

        var description = new NbtList(NbtTagType.String, "description")
        {
            new NbtTag<string>("translate", patternElement.Description.Translate)
        };

        writer.WriteString("template_item", patternElement.TemplateItem);
        writer.WriteString("asset_id", patternElement.AssetId);
        writer.WriteBool("decal", patternElement.Decal);
        writer.WriteTag(description);

    }
    #endregion

    #region Trim Material Writing
    public static void WriteElement(this TrimMaterialCodec value, NbtWriter writer)
    {
        var materialElement = value.Element;

        var description = new NbtList(NbtTagType.String, "description")
        {
            new NbtTag<string>("translate", materialElement.Description.Translate),
            new NbtTag<string>("color", materialElement.Description.Color!)
        };

        if (materialElement.OverrideArmorAssets is Dictionary<string, string> overrideArmorMats)
        {
            var overrideArmorAssets = new NbtCompound("override_armor_assets");

            foreach (var (type, replacement) in overrideArmorMats)
                overrideArmorAssets.Add(new NbtTag<string>(type, replacement));

            writer.WriteTag(overrideArmorAssets);
        }

        writer.WriteString("ingredient", materialElement.Ingredient);
        writer.WriteString("asset_name", materialElement.AssetName);
        writer.WriteTag(description);
    }
    #endregion

    #region Wolf Variant Writing

    public static void WriteElement(this WolfVariantCodec value, NbtWriter writer)
    {
        var materialElement = value.Element;

        writer.WriteString("tame_texture", materialElement.TameTexture);
        writer.WriteString("angry_texture", materialElement.AngryTexture);
        writer.WriteString("wild_texture", materialElement.WildTexture);
        writer.WriteString("biomes", materialElement.Biomes);
    }
    #endregion

    #region Painting Variant Writing
    public static void WriteElement(this PaintingVariantCodec value, NbtWriter writer)
    {
        var materialElement = value.Element;

        writer.WriteString("asset_id", materialElement.AssetId);
        writer.WriteInt("height", materialElement.Height);
        writer.WriteInt("width", materialElement.Width);
    }
    #endregion
}
