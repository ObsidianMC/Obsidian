using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.ArmorTrims;
using Obsidian.Nbt;
using System.Buffers.Binary;
using System.IO;

namespace Obsidian.API.Utilities;

public partial class Extensions
{
    private static NbtCompound ConsumeEffectToNbt(IConsumeEffect value)
    {
        var effect = value switch
        {
            ComponentConsumeEffect current => current,
            EffectWithProbability legacy => new ComponentConsumeEffect
            {
                Id = 0,
                Effects = [legacy.EffectData],
                Probability = legacy.Probability
            },
            _ => throw new NotSupportedException($"No saved codec for consume effect {value.Type}.")
        };
        var tag = new NbtCompound { new NbtTag<string>("type", effect.Type) };
        switch (effect.Id)
        {
            case 0:
                tag.Add(SavedList("effects", effect.Effects.Select(EffectToNbt)));
                tag.Add(new NbtTag<float>("probability", effect.Probability));
                break;
            case 1:
                tag.Add(SetToNbt("effects", effect.RemovedEffects, ComponentValueCodecs.MobEffects));
                break;
            case 2:
                break;
            case 3:
                tag.Add(new NbtTag<float>("diameter", effect.Diameter));
                break;
            case 4:
                tag.Add(SoundToNbt("sound", effect.Sound));
                break;
            default:
                throw new NotSupportedException($"No saved codec for consume effect {effect.Id}.");
        }

        return tag;
    }

    private static IConsumeEffect ConsumeEffectFromNbt(NbtCompound tag) => tag.SavedString("type") switch
    {
        "minecraft:apply_effects" => new ComponentConsumeEffect
        {
            Id = 0,
            Effects = [.. Compounds(tag, "effects").Select(EffectFromNbt)],
            Probability = SavedFloat(tag, "probability", 1)
        },
        "minecraft:remove_effects" => new ComponentConsumeEffect
        {
            Id = 1,
            RemovedEffects = SetFromNbt(tag["effects"], ComponentValueCodecs.MobEffects)
        },
        "minecraft:clear_all_effects" => new ComponentConsumeEffect { Id = 2 },
        "minecraft:teleport_randomly" => new ComponentConsumeEffect
        {
            Id = 3,
            Diameter = SavedFloat(tag, "diameter", 16)
        },
        "minecraft:play_sound" => new ComponentConsumeEffect
        {
            Id = 4,
            Sound = SoundFromNbt(tag["sound"])
        },
        _ => throw new InvalidDataException("Unknown consume effect.")
    };

    private static NbtCompound EffectToNbt(PotionEffectData effect)
    {
        var tag = new NbtCompound
        {
            new NbtTag<string>("id", ComponentRegistryNames.Name(ComponentValueCodecs.MobEffects, effect.Id)),
            new NbtTag<int>("amplifier", effect.Amplifier),
            new NbtTag<int>("duration", effect.Duration),
            new NbtTag<bool>("ambient", effect.Ambient),
            new NbtTag<bool>("show_particles", effect.ShowParticles),
            new NbtTag<bool>("show_icon", effect.ShowIcon)
        };
        if (effect.HiddenEffect is not null)
        {
            var hidden = EffectToNbt(effect.HiddenEffect);
            hidden.Remove("id");
            hidden.Name = "hidden_effect";
            tag.Add(hidden);
        }

        return tag;
    }

    private static PotionEffectData EffectFromNbt(NbtCompound tag) =>
        EffectDetailsFromNbt(tag, ComponentRegistryNames.Id(ComponentValueCodecs.MobEffects, tag.SavedString("id")!));

    private static PotionEffectData EffectDetailsFromNbt(NbtCompound tag, int id) => new()
    {
        Id = id,
        Amplifier = tag.TryGetTagValue<byte>("amplifier", out var amplifier)
            ? amplifier : SavedInt(tag, "amplifier"),
        Duration = SavedInt(tag, "duration"),
        Ambient = SavedBool(tag, "ambient"),
        ShowParticles = SavedBool(tag, "show_particles", true),
        ShowIcon = SavedBool(tag, "show_icon", SavedBool(tag, "show_particles", true)),
        HiddenEffect = tag.TryGetTag<NbtCompound>("hidden_effect", out var hidden)
            ? EffectDetailsFromNbt(hidden, id) : null
    };

    private static INbtTag InstrumentToNbt(string name, InstrumentDataComponent instrument)
    {
        if (instrument.RegistryId is int id)
            return new NbtTag<string>(name, ComponentRegistryNames.Name("minecraft:instrument", id));

        if (instrument.Value.Identifier is string identifier)
            return new NbtTag<string>(name, identifier);

        return new NbtCompound(name)
        {
            SoundToNbt("sound_event", instrument.Value.SoundEvent),
            new NbtTag<float>("use_duration", instrument.Value.UseDuration),
            new NbtTag<float>("range", instrument.Value.Range),
            instrument.Value.Description.ToNbt("description")
        };
    }

    private static InstrumentDataComponent InstrumentFromNbt(INbtTag tag)
    {
        if (tag is NbtTag<string> name)
        {
            var registered = InstrumentsRegistry.All.FirstOrDefault(value => value.Identifier == name.Value);
            return new InstrumentDataComponent
            {
                RegistryId = registered?.Id,
                IsResourceKey = registered is null,
                Value = registered is not null ? registered.ToInstrumentData() : new() { Identifier = name.Value }
            };
        }

        var value = (NbtCompound)tag;
        return new InstrumentDataComponent
        {
            Value = new InstrumentData
            {
                SoundEvent = SoundFromNbt(value["sound_event"]),
                UseDuration = value.GetFloat("use_duration"),
                Range = value.GetFloat("range"),
                Description = value["description"].TextFromNbt()!
            }
        };
    }

    private static INbtTag SongToNbt(string name, JukeboxPlayableDataComponent song)
    {
        if (song.Song.Left is string identifier)
            return new NbtTag<string>(name, identifier);

        if (song.RegistryId is int id)
            return new NbtTag<string>(name, ComponentRegistryNames.Name("minecraft:jukebox_song", id));

        var value = song.Song.Right!;
        return new NbtCompound(name)
        {
            SoundToNbt("sound_event", value.SoundEvent),
            value.Description.ToNbt("description"),
            new NbtTag<float>("length_in_seconds", value.LengthInSeconds),
            new NbtTag<int>("comparator_output", value.ComparatorOutput)
        };
    }

    private static JukeboxPlayableDataComponent SongFromNbt(INbtTag tag)
    {
        if (tag is NbtTag<string> name)
        {
            try
            {
                return new JukeboxPlayableDataComponent
                {
                    RegistryId = ComponentRegistryNames.Id("minecraft:jukebox_song", name.Value!)
                };
            }
            catch (InvalidDataException)
            {
                return new JukeboxPlayableDataComponent { Song = new(name.Value!) };
            }
        }

        var value = (NbtCompound)tag;
        return new JukeboxPlayableDataComponent
        {
            Song = new(new JukeboxSong
            {
                SoundEvent = SoundFromNbt(value["sound_event"]),
                Description = value["description"].TextFromNbt()!,
                LengthInSeconds = value.GetFloat("length_in_seconds"),
                ComparatorOutput = value.GetInt("comparator_output")
            })
        };
    }

    private static NbtList AttributesToNbt(string name, AttributeModifiersDataComponent attributes)
    {
        var list = new NbtList(NbtTagType.Compound, name);
        for (var index = 0; index < attributes.Value.Length; index++)
        {
            var modifier = attributes.Value[index];
            var tag = new NbtCompound
            {
                new NbtTag<string>("type", ComponentRegistryNames.Name(ComponentValueCodecs.Attributes, modifier.Id)),
                new NbtTag<string>("id", modifier.Name),
                new NbtTag<double>("amount", modifier.Value),
                new NbtTag<string>("operation", modifier.Operation switch
                {
                    AttributeOperation.Add => "add_value",
                    AttributeOperation.MulBase => "add_multiplied_base",
                    _ => "add_multiplied_total"
                }),
                new NbtTag<string>("slot", modifier.Slot.ToString().ToSnakeCase().Replace("_hand", "hand"))
            };
            var display = index < attributes.Displays.Length ? attributes.Displays[index] : default;
            var displayTag = new NbtCompound("display")
            {
                new NbtTag<string>("type", display.Type switch
                {
                    0 => "default",
                    1 => "hidden",
                    2 => "override",
                    _ => throw new ArgumentOutOfRangeException(nameof(attributes))
                })
            };
            if (display.Type == 2)
                displayTag.Add(display.Text!.ToNbt("value"));

            tag.Add(displayTag);
            list.Add(tag);
        }

        return list;
    }

    private static AttributeModifiersDataComponent AttributesFromNbt(NbtList list)
    {
        var modifiers = ImmutableArray.CreateBuilder<AttributeModifier>();
        var displays = ImmutableArray.CreateBuilder<(int, ChatMessage?)>();
        foreach (var tag in list.OfType<NbtCompound>())
        {
            modifiers.Add(new AttributeModifier
            {
                Id = ComponentRegistryNames.Id(ComponentValueCodecs.Attributes, tag.SavedString("type")!),
                Name = tag.SavedString("id")!,
                Value = tag.GetDouble("amount"),
                Operation = tag.SavedString("operation") switch
                {
                    "add_value" => AttributeOperation.Add,
                    "add_multiplied_base" => AttributeOperation.MulBase,
                    "add_multiplied_total" => AttributeOperation.MulTotal,
                    _ => throw new InvalidDataException("Unknown modifier operation.")
                },
                Slot = SavedEnum<AttributeSlot>(tag.SavedString("slot") ?? "any")
            });
            var display = tag.TryGetTag<NbtCompound>("display", out var saved) ? saved : null;
            displays.Add(display?.SavedString("type") switch
            {
                null or "default" => (0, null),
                "hidden" => (1, null),
                "override" => (2, display["value"].TextFromNbt()),
                _ => throw new InvalidDataException("Unknown attribute modifier display.")
            });
        }

        return new AttributeModifiersDataComponent
        {
            Value = modifiers.ToImmutable(),
            Displays = displays.ToImmutable()
        };
    }

    private static NbtCompound TrimToNbt(string name, TrimDataComponent trim)
    {
        var material = trim.MaterialRegistryId is int materialId
            ? (INbtTag)new NbtTag<string>(
                "material", ComponentRegistryNames.Name("minecraft:trim_material", materialId))
            : new NbtCompound("material")
            {
                new NbtTag<string>("palette_id", trim.Material.PaletteId),
                TrimText(trim.MaterialDescription, trim.Material.Description).ToNbt("description")
            };

        var pattern = trim.PatternRegistryId is int patternId
            ? (INbtTag)new NbtTag<string>("pattern", ComponentRegistryNames.Name("minecraft:trim_pattern", patternId))
            : new NbtCompound("pattern")
            {
                new NbtTag<string>("asset_id", trim.Pattern.AssetId),
                TrimText(trim.PatternDescription, trim.Pattern.Description).ToNbt("description"),
                new NbtTag<bool>("decal", trim.Pattern.Decal)
            };
        return new NbtCompound(name)
        {
            material,
            pattern
        };
    }

    private static ChatMessage TrimText(ChatMessage? text, TrimDescription description) => text ?? new ChatMessage
    {
        Translate = description.Translate,
        Color = description.Color is string color ? new HexColor(color) : null
    };

    private static TrimDescription TrimDescriptionFromNbt(INbtTag tag)
    {
        var text = tag.TextFromNbt()!;
        return new TrimDescription
        {
            Translate = text.Translate ?? "",
            Color = text.Color?.ToString()
        };
    }

    private static TrimDataComponent TrimFromNbt(NbtCompound tag)
    {
        var material = tag["material"];
        var pattern = tag["pattern"];
        return new TrimDataComponent
        {
            MaterialRegistryId = material is NbtTag<string> materialName
                ? ComponentRegistryNames.Id("minecraft:trim_material", materialName.Value!) : null,
            PatternRegistryId = pattern is NbtTag<string> patternName
                ? ComponentRegistryNames.Id("minecraft:trim_pattern", patternName.Value!) : null,
            Material = material is NbtCompound data ? new()
            {
                PaletteId = data.SavedString("palette_id")!,
                Description = TrimDescriptionFromNbt(data["description"])
            } : null!,
            MaterialDescription = material is NbtCompound materialData
                ? materialData["description"].TextFromNbt() : null,
            Pattern = pattern is NbtCompound patternData ? new()
            {
                AssetId = patternData.SavedString("asset_id")!,
                Description = TrimDescriptionFromNbt(patternData["description"]),
                Decal = SavedBool(patternData, "decal")
            } : null!,
            PatternDescription = pattern is NbtCompound description ? description["description"].TextFromNbt() : null
        };
    }

    private static NbtList PatternsToNbt(string name, BannerPatternsDataComponent patterns)
    {
        var list = new NbtList(NbtTagType.Compound, name);
        for (var i = 0; i < patterns.Value.Length; i++)
        {
            var pattern = patterns.Value[i];
            var id = i < patterns.RegistryIds.Length ? patterns.RegistryIds[i] : null;
            list.Add(new NbtCompound
            {
                id is int registryId
                    ? new NbtTag<string>(
                        "pattern", ComponentRegistryNames.Name("minecraft:banner_pattern", registryId))
                    : (INbtTag)new NbtCompound("pattern")
                    {
                        new NbtTag<string>("asset_id", pattern.AssetId),
                        new NbtTag<string>("translation_key", pattern.TranslationKey)
                    },
                new NbtTag<string>("color", pattern.Color.ToString().ToSnakeCase())
            });
        }

        return list;
    }

    private static BannerPatternsDataComponent PatternsFromNbt(NbtList list)
    {
        var layers = ImmutableArray.CreateBuilder<BannerPatternLayer>();
        var ids = ImmutableArray.CreateBuilder<int?>();
        foreach (var tag in list.OfType<NbtCompound>())
        {
            var pattern = tag["pattern"];
            ids.Add(pattern is NbtTag<string> name
                ? ComponentRegistryNames.Id("minecraft:banner_pattern", name.Value!) : null);
            layers.Add(new BannerPatternLayer
            {
                AssetId = pattern is NbtCompound inline ? inline.SavedString("asset_id")! : "",
                TranslationKey = pattern is NbtCompound data ? data.SavedString("translation_key")! : "",
                Color = SavedEnum<Dye>(tag.SavedString("color")!)
            });
        }

        return new BannerPatternsDataComponent
        {
            Value = layers.ToImmutable(),
            RegistryIds = ids.ToImmutable()
        };
    }

    private static NbtCompound ProfileToNbt(string name, ProfileDataComponent profile)
    {
        var tag = new NbtCompound(name);
        if (profile.Username is not null)
            tag.Add(new NbtTag<string>("name", profile.Username));

        if (profile.Id is Guid id)
        {
            var bytes = id.ToByteArray(bigEndian: true);
            tag.Add(new NbtArray<int>("id", Enumerable.Range(0, 4)
                .Select(index => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(index * 4, 4))).ToArray()));
        }

        tag.Add(SavedList("properties", profile.Properties.Select(property =>
        {
            var value = new NbtCompound
            {
                new NbtTag<string>("name", property.Name),
                new NbtTag<string>("value", property.Value)
            };
            if (property.Signature is not null)
                value.Add(new NbtTag<string>("signature", property.Signature));

            return value;
        })));
        if (profile.BodyTexture is not null)
            tag.Add(new NbtTag<string>("texture", profile.BodyTexture));

        if (profile.CapeTexture is not null)
            tag.Add(new NbtTag<string>("cape", profile.CapeTexture));

        if (profile.ElytraTexture is not null)
            tag.Add(new NbtTag<string>("elytra", profile.ElytraTexture));

        if (profile.Model is int model)
            tag.Add(new NbtTag<string>("model", model == 1 ? "slim" : "wide"));

        return tag;
    }

    private static ProfileDataComponent ProfileFromNbt(NbtCompound tag)
    {
        Guid? id = null;
        if (tag.TryGetTag<NbtArray<int>>("id", out var uuid))
        {
            var bytes = new byte[16];
            for (var i = 0; i < 4; i++)
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(i * 4, 4), uuid[i]);

            id = new Guid(bytes, bigEndian: true);
        }

        var username = tag.SavedString("name");
        return new ProfileDataComponent
        {
            Id = id,
            Username = username,
            IsComplete = id is not null && username is not null,
            Properties = [.. Compounds(tag, "properties").Select(property => new SkinProperty
            {
                Name = property.SavedString("name")!,
                Value = property.SavedString("value")!,
                Signature = property.SavedString("signature")
            })],
            BodyTexture = tag.SavedString("texture"),
            CapeTexture = tag.SavedString("cape"),
            ElytraTexture = tag.SavedString("elytra"),
            Model = tag.SavedString("model") is string model ? model == "slim" ? 1 : 0 : null
        };
    }
}
