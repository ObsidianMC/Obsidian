using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Redstone;
using Obsidian.WorldData.Portals;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Processors;

namespace Obsidian.WorldData;

/// <summary>Structure editor actions and rising-edge redstone triggers.</summary>
internal static class StructureBlocks
{
    internal static string NormalizeName(string name)
    {
        if (name.Length == 0)
            return string.Empty;

        var colon = name.IndexOf(':');
        var ns = colon <= 0 ? "minecraft" : name[..colon];
        var path = colon < 0 ? name : name[(colon + 1)..];
        if (!ns.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.')
            || !path.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.' or '/'))
        {
            return string.Empty;
        }

        return ns + ":" + path;
    }

    internal static StructureBlockMode ModeOf(IBlock block) => block.GetProperty("mode") switch
    {
        "save" => StructureBlockMode.Save,
        "corner" => StructureBlockMode.Corner,
        "data" => StructureBlockMode.Data,
        _ => StructureBlockMode.Load
    };

    internal static void UpdateMode(AbstractLevel level, StructureBlockEntity entity)
    {
        var position = entity.BlockPosition;
        var state = level.BlockUpdates.GetBlock(position);
        if (!state.Is(BlockIds.StructureBlock) || !ReferenceEquals(level.GetResidentBlockEntity(position), entity))
            return;

        var mode = StructureBlockEntity.ModeNames[(int)entity.Settings.Mode].ToLowerInvariant();
        level.BlockUpdates.Set(position, state.WithProperty("mode", mode), BlockUpdateFlags.Clients);
    }

    internal static void OnPlacedByPlayer(AbstractLevel level, Vector position, IClientPlayer player)
    {
        lock (level.Fluids.TickLock)
        {
            if (level.GetResidentBlockEntity(position) is not StructureBlockEntity entity)
                return;

            entity.Settings = entity.Settings with { Author = player.Username };
            entity.SetChanged();
        }
    }

    internal static async Task<string?> EditAsync(AbstractLevel level, Vector position, StructureBlockSettings edit,
        int action, string suppliedName)
    {
        StructureBlockEntity entity;
        StructureBlockSettings settings;
        lock (level.Fluids.TickLock)
        {
            if (level.GetResidentBlockEntity(position) is not StructureBlockEntity current)
                return null;

            entity = current;
            settings = edit with
            {
                Author = entity.Settings.Author,
                Powered = entity.Settings.Powered,
                Revision = entity.Settings.Revision + 1
            };
            entity.Settings = settings;
            UpdateMode(level, entity);
            Changed(entity);
        }

        var name = settings.Name;
        if (name.Length == 0)
            return $"Invalid structure name '{suppliedName}'";

        if (!StructureTemplateNames.IsValid(name))
        {
            return action switch
            {
                1 => $"Unable to save structure '{name}'",
                2 => $"Structure '{name}' is not available",
                3 => "Unable to detect structure size. Add corners with matching structure names",
                _ => null
            };
        }

        if (action == 0)
            return null;

        if (action == 3)
        {
            using var area = settings.Mode == StructureBlockMode.Save
                ? await LoadAreaAsync(level, position + new Vector(-80, 0, -80), new Vector(161, 1, 161))
                : null;

            lock (level.Fluids.TickLock)
            {
                if (!Current(level, entity, settings))
                    return null;

                var detected = DetectSize(level, entity);
                Changed(entity);
                return detected
                    ? $"Size successfully detected for '{name}'"
                    : "Unable to detect structure size. Add corners with matching structure names";
            }
        }

        if (action == 1)
        {
            if (settings.Mode != StructureBlockMode.Save)
                return $"Unable to save structure '{name}'";

            var templates = StructureBlockTemplates.For(level);
            // A zero-sized save retains the previous template, even if it was only present on disk.
            if (settings.Size.X < 1 || settings.Size.Y < 1 || settings.Size.Z < 1)
                await templates.ReadAsync(name);

            using var area = await LoadAreaAsync(level, position + settings.Offset, settings.Size);
            Task<bool> saving;
            lock (level.Fluids.TickLock)
            {
                if (!Current(level, entity, settings))
                    return null;

                var template = Capture(level, position, settings, templates);
                saving = templates.WriteAsync(name, template);
            }

            return await saving ? $"Structure saved as '{name}'" : $"Unable to save structure '{name}'";
        }

        if (settings.Mode != StructureBlockMode.Load)
            return $"Structure '{name}' is not available";

        return await LoadAsync(level, entity, settings, prepareSize: true);
    }

    private static StructureTemplate Capture(AbstractLevel level, Vector position, StructureBlockSettings settings,
        StructureBlockTemplates templates)
    {
        var size = settings.Size;
        var template = size.X < 1 || size.Y < 1 || size.Z < 1
            ? templates.Cached(settings.Name) ?? StructureTemplate.CreateEmpty()
            : StructureTemplate.Capture(level, position + settings.Offset, size,
                !settings.IgnoreEntities, settings.Author);
        template = template.WithAuthor(settings.Author);
        templates.Put(settings.Name, template);
        return template;
    }

    private static async Task<string?> LoadAsync(AbstractLevel level, StructureBlockEntity entity,
        StructureBlockSettings settings, bool prepareSize)
    {
        var template = await StructureBlockTemplates.For(level).ReadAsync(settings.Name);
        if (template is null)
            return $"Structure '{settings.Name}' is not available";

        lock (level.Fluids.TickLock)
        {
            if (!Current(level, entity, settings))
                return null;

            if (prepareSize && settings.Size != template.Size)
            {
                UpdateInfo(entity, template);
                return $"Structure '{settings.Name}' position prepared";
            }
        }

        var placement = Placement(settings);
        var origin = entity.BlockPosition + settings.Offset;
        var box = template.GetBoundingBox(placement, origin);
        using var area = await LoadAreaAsync(level, new Vector(box.MinX, box.MinY, box.MinZ),
            new Vector(box.MaxX - box.MinX + 1, box.MaxY - box.MinY + 1, box.MaxZ - box.MinZ + 1));
        lock (level.Fluids.TickLock)
        {
            if (!Current(level, entity, settings))
                return null;

            Place(level, entity, settings, template, placement);
        }

        return $"Structure loaded from '{settings.Name}'";
    }

    private static void UpdateInfo(StructureBlockEntity entity, StructureTemplate template)
    {
        entity.Settings = entity.Settings with { Size = template.Size, Author = template.Author };
        Changed(entity);
    }

    private static void Place(AbstractLevel level, StructureBlockEntity entity, StructureBlockSettings settings,
        StructureTemplate template, StructurePlaceSettings placement)
    {
        UpdateInfo(entity, template);
        var origin = entity.BlockPosition + settings.Offset;
        var view = new StructureBlockLevel(level, settings.Strict);
        template.PlaceInWorld(view, origin, origin, placement, Random(settings.Seed));
    }

    internal static StructurePlaceSettings Placement(StructureBlockSettings settings)
    {
        var placement = new StructurePlaceSettings
        {
            Mirror = settings.Mirror,
            Rotation = settings.Rotation,
            IgnoreEntities = settings.IgnoreEntities,
            KnownShape = settings.Strict
        };
        if (settings.Integrity < 1)
        {
            placement.Processors.Add(new BlockRotProcessor { Integrity = Math.Clamp(settings.Integrity, 0, 1) });
            placement.Random = Random(settings.Seed);
        }

        return placement;
    }

    internal static IRandomSource Random(long seed) =>
        new LegacyRandomSource(seed == 0 ? Environment.TickCount64 : seed);

    private static bool Current(AbstractLevel level, StructureBlockEntity entity, StructureBlockSettings settings) =>
        !entity.IsRemoved && entity.Settings.Revision == settings.Revision
        && ReferenceEquals(level.GetResidentBlockEntity(entity.BlockPosition), entity);

    private static void Changed(StructureBlockEntity entity)
    {
        entity.SetChanged();
        entity.SendUpdate();
    }

    internal static async Task<PortalArea> LoadAreaAsync(AbstractLevel level, Vector origin, Vector size)
    {
        // The existing generation waiter owns chunk tickets and can start generation while an entity action is
        // awaiting it. Keep the lease through the snapshot/placement so chunk management can't unload its inputs.
        var width = Math.Max(size.X, 1);
        var depth = Math.Max(size.Z, 1);
        var center = origin + new Vector((width - 1) / 2, 0, (depth - 1) / 2);
        var radius = Math.Max(width, depth) / 2;
        return await PortalArea.LoadAsync(level, center, radius, () => !level.IsDisposed)
            ?? throw new OperationCanceledException("The structure block's level closed");
    }

    private static bool AreaLoaded(AbstractLevel level, Vector origin, Vector size)
    {
        for (var x = origin.X >> 4; x <= (origin.X + Math.Max(size.X, 1) - 1) >> 4; x++)
        {
            for (var z = origin.Z >> 4; z <= (origin.Z + Math.Max(size.Z, 1) - 1) >> 4; z++)
            {
                if (level.GetLoadedChunk(x, z) is not { IsGenerated: true })
                    return false;
            }
        }

        return true;
    }

    private static bool DetectSize(AbstractLevel level, StructureBlockEntity entity)
    {
        var settings = entity.Settings;
        if (settings.Mode != StructureBlockMode.Save)
            return false;

        var self = entity.BlockPosition;
        var corners = new List<Vector>();
        for (var x = (self.X - 80) >> 4; x <= (self.X + 80) >> 4; x++)
        {
            for (var z = (self.Z - 80) >> 4; z <= (self.Z + 80) >> 4; z++)
            {
                var chunk = level.GetLoadedChunk(x, z);
                if (chunk is null)
                    continue;

                foreach (var blockEntity in chunk.GetBlockEntities())
                {
                    if (blockEntity is not StructureBlockEntity corner
                        || corner.Settings.Mode != StructureBlockMode.Corner
                        || corner.Settings.Name != settings.Name)
                    {
                        continue;
                    }

                    var position = corner.BlockPosition;
                    if (Math.Abs(position.X - self.X) <= 80 && Math.Abs(position.Z - self.Z) <= 80)
                        corners.Add(position);
                }
            }
        }

        if (corners.Count == 0)
            return false;

        if (corners.Count == 1)
            corners.Add(self);

        var min = new Vector(corners.Min(at => at.X), corners.Min(at => at.Y), corners.Min(at => at.Z));
        var max = new Vector(corners.Max(at => at.X), corners.Max(at => at.Y), corners.Max(at => at.Z));
        var size = max - min - 1;
        if (size.X < 1 || size.Y < 1 || size.Z < 1)
            return false;

        entity.Settings = entity.Settings with { Offset = min - self + 1, Size = size };
        return true;
    }

    internal static void Register(IRedstoneLevel world, NeighborBehaviors neighbors, RedstoneEngine engine)
    {
        if (world is AbstractLevel level)
            neighbors.Register(BlockIds.StructureBlock, new PowerReaction(level, engine));
    }

    private sealed class PowerReaction(AbstractLevel level, RedstoneEngine engine) : INeighborChangedBehavior
    {
        public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
        {
            if (level.GetResidentBlockEntity(change.Position) is not StructureBlockEntity entity)
                return ValueTask.CompletedTask;

            var powered = engine.HasNeighborSignal(change.Position);
            var settings = entity.Settings;
            if (settings.Powered == powered)
                return ValueTask.CompletedTask;

            settings = settings with { Powered = powered };
            entity.Settings = settings;
            entity.SetChanged();
            if (!powered || !StructureTemplateNames.IsValid(settings.Name))
                return ValueTask.CompletedTask;

            var templates = StructureBlockTemplates.For(level);
            switch (settings.Mode)
            {
                case StructureBlockMode.Save:
                    if (AreaLoaded(level, change.Position + settings.Offset, settings.Size))
                    {
                        Capture(level, change.Position, settings, templates);
                    }
                    else
                    {
                        level.EnqueueEntityAction(async () =>
                        {
                            using var area = await LoadAreaAsync(level,
                                change.Position + settings.Offset, settings.Size);
                            lock (level.Fluids.TickLock)
                            {
                                if (Current(level, entity, settings))
                                    Capture(level, change.Position, settings, templates);
                            }
                        });
                    }

                    break;
                case StructureBlockMode.Load:
                    var template = templates.Cached(settings.Name);
                    var placement = Placement(settings);
                    var box = template?.GetBoundingBox(placement, change.Position + settings.Offset);
                    if (template is not null && box is BlockBox bounds
                        && AreaLoaded(level, new Vector(bounds.MinX, bounds.MinY, bounds.MinZ),
                            new Vector(bounds.MaxX - bounds.MinX + 1, bounds.MaxY - bounds.MinY + 1,
                                bounds.MaxZ - bounds.MinZ + 1)))
                    {
                        Place(level, entity, settings, template, placement);
                        break;
                    }

                    level.EnqueueEntityAction(async () =>
                    {
                        await LoadAsync(level, entity, settings, prepareSize: false);
                    });
                    break;
                case StructureBlockMode.Corner:
                    templates.Remove(settings.Name);
                    break;
            }

            return ValueTask.CompletedTask;
        }
    }
}
