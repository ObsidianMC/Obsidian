using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using System.Buffers.Binary;

namespace Obsidian.Entities;

/// <summary>
/// Saves and loads entities in vanilla's NBT form (<c>Entity.saveAsPassenger</c>, <c>EntityType.create</c>), for chunks'
/// entity storage.
/// </summary>
internal static class EntityNbt
{
    /// <summary>
    /// Saves <paramref name="entity"/> with its <c>id</c>, or returns <c>null</c> for entities that aren't saved: players
    /// (they have their own files), types Obsidian can't restore, and transient text displays.
    /// </summary>
    public static NbtCompound? Save(Entity entity)
    {
        if (entity.Type is EntityType type && !IsSaved(type)
            || entity is Mob { HasAi: true } mob && (!mob.Alive && mob is not EnderDragon || mob.IsRemoved)
            || entity is TextDisplay { Transient: true })
        {
            return null;
        }

        var tag = new NbtCompound { new NbtTag<string>("id", TypeId(entity)) };
        foreach (var (name, child) in entity.UnmodeledData)
        {
            if (name != "id")
                tag.Add(name, child);
        }

        entity.WriteNbt(tag);
        return tag;
    }

    /// <summary>
    /// Creates the entity a saved compound describes, without spawning it, or returns <c>null</c> when its type is
    /// unknown or isn't saved, or when it's an item entity without an item (which vanilla discards too).
    /// </summary>
    public static Entity? Load(NbtCompound tag, ILevel level)
    {
        if (!tag.TryGetTag<NbtTag<string>>("id", out var id))
            return null;

        // A plugin's entity, while its plugin is loaded; one whose plugin is gone stays as it was saved.
        if (EntityDefinitionsRegistry.TryGet(id.Value!, out var definition))
            return definition.Binding is null ? null : Restore(PluginEntity.Create(definition, level), tag);

        if (!TryParseType(id.Value, out var type) || !IsSaved(type))
            return null;

        Entity entity = type switch
        {
            EntityType.AreaEffectCloud when tag.TryGetBool("ObsidianDragonBreath", out var breath) && breath => new DragonBreathCloud { Level = level },
            EntityType.ItemFrame => new ItemFrame { Level = level },
            EntityType.GlowItemFrame => new GlowItemFrame { Level = level },
            EntityType.Painting => new Painting { Level = level },
            EntityType.Item => new ItemEntity { Level = level },
            _ => EntitySpawner.Create(type, level)
        };

        entity.Type = type;
        return Restore(entity, tag);
    }

    // Gives a created entity an id and its saved fields.
    private static Entity? Restore(Entity entity, NbtCompound tag)
    {
        entity.EntityId = Server.GetNextEntityId();

        var unmodeled = new NbtCompound();
        foreach (var (name, child) in tag)
        {
            if (name != "id")
                unmodeled.Add(name, child);
        }

        entity.UnmodeledData = unmodeled;
        entity.ReadNbt(tag);

        return entity is ItemEntity { Item: null } ? null : entity;
    }

    /// <summary>
    /// An entity placed by world generation in the saved form: its fields with the <c>id</c>, <c>Pos</c> and
    /// <c>Rotation</c>.
    /// </summary>
    public static NbtCompound ToNbt(GeneratedEntity entity)
    {
        var tag = new NbtCompound
        {
            new NbtTag<string>("id", entity.Type),
            DoubleList("Pos", entity.Position),
            new NbtList(NbtTagType.Float, "Rotation")
            {
                new NbtTag<float>(string.Empty, entity.Yaw),
                new NbtTag<float>(string.Empty, entity.Pitch)
            }
        };

        foreach (var (name, child) in entity.Data)
        {
            if (name is not ("id" or "Pos" or "Rotation"))
                tag.Add(name, child);
        }

        return tag;
    }

    /// <summary>
    /// Reads an entity saved by <see cref="ToNbt(GeneratedEntity)"/> or <see cref="Save"/> back as a
    /// <see cref="GeneratedEntity"/>, or returns <c>null</c> without an <c>id</c> or position.
    /// </summary>
    public static GeneratedEntity? ToGeneratedEntity(NbtCompound tag)
    {
        if (!tag.TryGetTag<NbtTag<string>>("id", out var id) || !TryReadVector(tag, "Pos", out var position))
            return null;

        var yaw = 0f;
        var pitch = 0f;
        if (tag.TryGetTag<NbtList>("Rotation", out var rotation) && rotation.Count >= 2
            && rotation[0] is NbtTag<float> yawTag && rotation[1] is NbtTag<float> pitchTag)
        {
            yaw = yawTag.Value;
            pitch = pitchTag.Value;
        }

        var data = new NbtCompound();
        foreach (var (name, child) in tag)
        {
            if (name is not ("id" or "Pos" or "Rotation"))
                data.Add(name, child);
        }

        return new GeneratedEntity(id.Value!, position, yaw, pitch) { Data = data };
    }

    /// <summary>
    /// The vanilla id of an entity type, e.g. <c>minecraft:end_crystal</c> for <see cref="EntityType.EndCrystal"/>.
    /// </summary>
    public static string TypeId(EntityType type) => $"minecraft:{type.ToString().ToSnakeCase()}";

    /// <summary>
    /// The id an entity is saved with: its vanilla type's, or its plugin type's identifier.
    /// </summary>
    public static string TypeId(Entity entity) => entity.Definition?.Identifier
        ?? TypeId(entity.Type ?? throw new InvalidOperationException($"Entity {entity.EntityId} has no type."));

    /// <summary>
    /// Whether a saved id is of an entity type the server knows: a vanilla type or a plugin's registered now.
    /// </summary>
    public static bool IsKnownType(string? id) =>
        id is not null && (EntityDefinitionsRegistry.TryGet(id, out _) || TryParseType(id, out _));

    /// <summary>
    /// The entity type of a vanilla id, in vanilla's namespace or without one; the inverse of <see cref="TypeId"/>.
    /// </summary>
    public static bool TryParseType(string? id, out EntityType type)
    {
        type = default;
        if (id is null)
            return false;

        var colon = id.IndexOf(':');
        if (colon >= 0 && id[..colon] != "minecraft")
            return false;

        return Enum.TryParse(id[(colon + 1)..].Replace("_", string.Empty), ignoreCase: true, out type);
    }

    /// <summary>
    /// Sets a named tag, replacing any previous value.
    /// </summary>
    public static void Set(this NbtCompound compound, INbtTag tag)
    {
        compound.Remove(tag.Name!);
        compound.Add(tag);
    }

    /// <summary>
    /// Sets <paramref name="tag"/>, or removes the field when it's <c>null</c>.
    /// </summary>
    public static void SetOrRemove(this NbtCompound compound, string name, INbtTag? tag)
    {
        compound.Remove(name);
        if (tag is not null)
            compound.Add(name, tag);
    }

    /// <summary>
    /// Sets a boolean field that's only saved when <c>true</c>.
    /// </summary>
    public static void SetFlag(this NbtCompound compound, string name, bool value) =>
        compound.SetOrRemove(name, value ? new NbtTag<bool>(name, true) : null);

    public static NbtList DoubleList(string name, VectorD vector) => new(NbtTagType.Double, name)
    {
        new NbtTag<double>(string.Empty, vector.X),
        new NbtTag<double>(string.Empty, vector.Y),
        new NbtTag<double>(string.Empty, vector.Z)
    };

    public static bool TryReadVector(NbtCompound compound, string name, out VectorD vector)
    {
        if (compound.TryGetTag<NbtList>(name, out var list) && list.Count >= 3
            && list[0].TryGetNumber(out double x)
            && list[1].TryGetNumber(out double y)
            && list[2].TryGetNumber(out double z))
        {
            vector = new VectorD(x, y, z);
            return true;
        }

        vector = default;
        return false;
    }

    /// <summary>
    /// Vanilla's <c>UUIDUtil.CODEC</c>: the UUID's 128 bits as four big-endian ints, most significant first.
    /// </summary>
    public static int[] UuidToInts(Guid uuid)
    {
        Span<byte> bytes = stackalloc byte[16];
        uuid.TryWriteBytes(bytes, bigEndian: true, out _);

        return
        [
            BinaryPrimitives.ReadInt32BigEndian(bytes),
            BinaryPrimitives.ReadInt32BigEndian(bytes[4..]),
            BinaryPrimitives.ReadInt32BigEndian(bytes[8..]),
            BinaryPrimitives.ReadInt32BigEndian(bytes[12..])
        ];
    }

    public static Guid UuidFromInts(ReadOnlySpan<int> ints)
    {
        Span<byte> bytes = stackalloc byte[16];
        for (var i = 0; i < 4; i++)
            BinaryPrimitives.WriteInt32BigEndian(bytes[(i * 4)..], ints[i]);

        return new Guid(bytes, bigEndian: true);
    }

    // Players have their own files; Obsidian can't restore falling blocks (they need their block state) or experience
    // orbs, and vanilla never saves fishing bobbers or lightning.
    private static bool IsSaved(EntityType type) => type is not (EntityType.Player or EntityType.FallingBlock
        or EntityType.ExperienceOrb or EntityType.FishingBobber or EntityType.LightningBolt or EntityType.LeashKnot);
}
