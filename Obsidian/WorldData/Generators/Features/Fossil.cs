using Obsidian.WorldData.Features;
using Obsidian.WorldData.Structures;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature Fossil(JsonElement json)
    {
        var fossils = json.GetProperty("fossil_structures").EnumerateArray()
            .Select(entry => entry.GetString()!).ToArray();
        var overlays = json.GetProperty("overlay_structures").EnumerateArray()
            .Select(entry => entry.GetString()!).ToArray();
        var fossilProcessors = this.FossilProcessors(json.GetProperty("fossil_processors"));
        var overlayProcessors = this.FossilProcessors(json.GetProperty("overlay_processors"));
        var maxEmpty = json.GetProperty("max_empty_corners_allowed").GetInt32();

        return (context, random, origin) =>
        {
            var level = context.Level;
            var rotation = StructureRotationExtensions.Random(random);
            var index = random.NextInt(fossils.Length);
            var fossil = this.Template(fossils[index]);
            var overlay = this.Template(overlays[index]);
            var chunkX = origin.X >> 4 << 4;
            var chunkZ = origin.Z >> 4 << 4;
            var settings = new StructurePlaceSettings
            {
                Rotation = rotation,
                Random = random,
                BoundingBox = new BlockBox(
                    new Vector(chunkX - 16, level.MinY, chunkZ - 16),
                    new Vector(chunkX + 31, level.MinY + level.Height - 1, chunkZ + 31))
            };
            var size = fossil.GetSize(rotation);
            var corner = origin + new Vector(-size.X / 2, 0, -size.Z / 2);
            var surface = origin.Y;

            for (var x = 0; x < size.X; x++)
            {
                for (var z = 0; z < size.Z; z++)
                {
                    var height = level.GetHeight(HeightmapType.OceanFloorWG, corner.X + x, corner.Z + z);
                    surface = Math.Min(surface, height);
                }
            }

            // 26.3 still draws burial depth after rotation/index and before either palette or processor pass.
            var y = Math.Max(surface - 15 - random.NextInt(10), level.MinY + 10);
            var zero = fossil.GetZeroPositionWithTransform(corner.AtY(y), rotation);
            var bounds = fossil.GetBoundingBox(settings, zero);
            var empty = bounds.Corners().Count(position =>
            {
                var state = level.GetBlock(position);
                return state.IsAir || state.RegistryId is BlockIds.Water or BlockIds.Lava;
            });

            if (empty > maxEmpty)
            {
                return false;
            }

            settings.SetProcessors(fossilProcessors);
            fossil.PlaceInWorld(level, zero, zero, settings, random);
            settings.SetProcessors(overlayProcessors);
            overlay.PlaceInWorld(level, zero, zero, settings, random);
            return true;
        };
    }

    private StructureProcessorList FossilProcessors(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.String)
        {
            json = this.Data.Read("processor_list", json.GetString()!);
        }

        var reader = new WorldgenReader(
            WorldgenTypes.BuiltIn,
            _ => throw new InvalidOperationException("A fossil processor cannot resolve a configured feature."),
            _ => throw new InvalidOperationException("A fossil processor cannot resolve a placed feature."));

        return reader.ReadProcessorList(TemplateProcessorStates(json), "");
    }
}
