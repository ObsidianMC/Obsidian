using Obsidian.API.World;
using Obsidian.Entities;

namespace Obsidian.WorldData.Portals;

internal readonly record struct PortalContact(Vector Position, int BlockId, PortalType? Type);
internal sealed record PortalDestination(
    AbstractLevel Level,
    VectorD Position,
    Angle Yaw,
    Angle Pitch,
    VectorD Motion,
    PortalArea Area,
    PortalType? Type = null,
    int Generation = 0);

/// <summary>Rectangular transit shared by players and other entities; preparation does not block ticks.</summary>
internal static class PortalTravel
{
    /// <summary>
    /// The portal block the entity touches, if any. <paramref name="includeEnd"/> adds End portals and
    /// <paramref name="includeGateways"/> End gateways, whose contacts have no <see cref="PortalType"/>.
    /// </summary>
    internal static async ValueTask<PortalContact?> FindContactAsync(
        IEntity entity, bool includeEnd = false, bool includeGateways = false)
    {
        var width = entity.Dimension.Width > 0 ? entity.Dimension.Width : 0.6;
        var height = entity.Dimension.Height > 0 ? entity.Dimension.Height
            : entity.Swimming ? 0.6 : entity.Sneaking ? 1.5 : 1.8;
        var min = new VectorD(entity.Position.X - width / 2, entity.Position.Y, entity.Position.Z - width / 2);
        var max = new VectorD(
            entity.Position.X + width / 2, entity.Position.Y + height, entity.Position.Z + width / 2);
        for (var x = (int)Math.Floor(min.X + 1e-7); x <= (int)Math.Floor(max.X - 1e-7); x++)
            for (var y = (int)Math.Floor(min.Y + 1e-7); y <= (int)Math.Floor(max.Y - 1e-7); y++)
                for (var z = (int)Math.Floor(min.Z + 1e-7); z <= (int)Math.Floor(max.Z - 1e-7); z++)
                {
                    var position = new Vector(x, y, z);
                    var block = await entity.Level.GetBlockAsync(position);
                    if (includeEnd && block?.RegistryId == BlockIds.EndPortal
                        && min.Y < y + 0.75 && max.Y > y + 0.375)
                        return new(position, BlockIds.EndPortal, null);

                    // End gateways are whole blocks to entities inside them.
                    if (includeGateways && block?.RegistryId == BlockIds.EndGateway)
                        return new(position, BlockIds.EndGateway, null);

                    if (block is null || PortalTypesRegistry.ForBlock(block.RegistryId) is not PortalType type)
                        continue;

                    // Vanilla's thin portal contact shape is a quarter block wide, centred in its plane.
                    if (block.GetProperty("axis") == "x"
                        ? min.Z < z + 0.625 && max.Z > z + 0.375
                        : min.X < x + 0.625 && max.X > x + 0.375)
                        return new(position, block.RegistryId, type);
                }

        return null;
    }

    internal static async Task<PortalDestination?> PrepareAsync(
        Entity entity, PortalType type, Vector entrance, Func<bool> canContinue)
    {
        if (!PortalTypesRegistry.TryEnter(type, out var properties, out var call))
            return null;

        using var behavior = call;
        var source = entity.Level;
        var generation = type.Generation;
        if (properties.ResolveTarget(source) is not AbstractLevel destination)
            return null;

        bool CanContinue() => canContinue() && entity.Level == source
            && type.Generation == generation && PortalTypesRegistry.IsActive(type, properties);
        try
        {
            var entranceBlock = await source.GetBlockAsync(entrance);
            var axis = entranceBlock?.GetProperty("axis") ?? "x";
            var sourceFrame = await LevelPortals.RectangleAsync(source.GetBlockAsync, entrance, axis, properties);
            var scale = properties.CoordinateScale?.Invoke(source, destination)
                ?? source.DimensionType.Element.CoordinateScale / destination.DimensionType.Element.CoordinateScale;
            if (!double.IsFinite(scale) || scale <= 0)
                throw new InvalidOperationException("Portal coordinate scale must be finite and positive.");
            var targetX = Math.Clamp(
                entity.Position.X * scale, -LevelPortals.WorldBorderLimit + 1, LevelPortals.WorldBorderLimit - 2);
            var targetZ = Math.Clamp(
                entity.Position.Z * scale, -LevelPortals.WorldBorderLimit + 1, LevelPortals.WorldBorderLimit - 2);
            var target = new Vector(
                (int)Math.Floor(targetX), (int)Math.Floor(entity.Position.Y), (int)Math.Floor(targetZ));
            var width = entity.Dimension.Width > 0 ? entity.Dimension.Width : 0.6;
            var height = entity.Dimension.Height > 0 ? entity.Dimension.Height : 1.8;
            // Vanilla's relative portal coordinates retain the entity's location within the entrance rectangle.
            var along = axis == "x"
                ? entity.Position.X - sourceFrame.Origin.X
                : entity.Position.Z - sourceFrame.Origin.Z;
            var horizontal = sourceFrame.Width > width
                ? Math.Clamp((along - width / 2) / (sourceFrame.Width - width), 0, 1)
                : 0.5;
            var vertical = sourceFrame.Height > height
                ? Math.Clamp((entity.Position.Y - sourceFrame.Origin.Y) / (sourceFrame.Height - height), 0, 1)
                : 0;
            var normal = axis == "x"
                ? entity.Position.Z - sourceFrame.Origin.Z - 0.5
                : entity.Position.X - sourceFrame.Origin.X - 0.5;
            var frame = await destination.Portals.FindOrCreateExitAsync(target, axis, CanContinue, properties);
            if (frame is null || !CanContinue())
                return null;
            var exit = frame.Value;
            var h = width / 2 + (exit.Width - width) * horizontal;
            var v = (exit.Height - height) * vertical;
            var positionAtExit = new VectorD(
                exit.Origin.X + (exit.Axis == "x" ? h : 0.5 + normal),
                exit.Origin.Y + v,
                exit.Origin.Z + (exit.Axis == "z" ? h : 0.5 + normal));
            var rotation = axis == exit.Axis ? 0f : 90f;
            var motion = rotation == 0
                ? entity.Motion : new VectorD(-entity.Motion.Z, entity.Motion.Y, entity.Motion.X);
            var exitArea = await PortalArea.LoadAsync(destination, (Vector)positionAtExit, 5, CanContinue);
            if (exitArea is null)
                return null;
            try
            {
                positionAtExit = PortalCollision.FindFreePosition(exitArea.Read, positionAtExit, width, height);
                if (CanContinue())
                    return new(
                        destination,
                        positionAtExit,
                        (float)entity.Yaw + rotation,
                        entity.Pitch,
                        motion,
                        exitArea,
                        type,
                        generation);
                exitArea.Dispose();
                return null;
            }
            catch
            {
                exitArea.Dispose();
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
