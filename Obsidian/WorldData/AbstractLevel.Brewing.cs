using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.WindowProperties;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla's LevelEvent.SOUND_BREWING_STAND_BREW, which clients play when a brew ends.
    private const int BrewingStandBrewEvent = 1035;

    private static readonly string[] bottleProperties = ["has_bottle_0", "has_bottle_1", "has_bottle_2"];

    /// <summary>
    /// Ticks the brewing stands of the chunks that tick, like vanilla ticks block entities: the stands players opened
    /// (a stand only gets its block entity then) whose block is still there.
    /// </summary>
    internal async ValueTask TickBrewingStandsAsync()
    {
        foreach (var chunk in Regions.Values.SelectMany(region => region.GeneratedChunks()).OfType<Chunk>())
        {
            if (chunk.BlockEntities.IsEmpty || !IsMobTicking(new VectorD(chunk.X * 16 + 8, 0, chunk.Z * 16 + 8)))
                continue;

            foreach (var blockEntity in chunk.BlockEntities.Values)
            {
                if (blockEntity is BrewingStand stand
                    && GameEventBlock(stand.BlockPosition)?.RegistryId == BlockIds.BrewingStand)
                {
                    await TickBrewingStandAsync(stand);
                }
            }
        }
    }

    private async ValueTask TickBrewingStandAsync(BrewingStand stand)
    {
        Brewing.Step step;
        using (await stand.LockAsync())
            step = Brewing.Tick(stand);

        var position = stand.BlockPosition;

        if (step.Brewed)
            BroadcastLevelEvent(BrewingStandBrewEvent, position, 0);

        if (step.Remainder is ItemStack remainder)
        {
            SpawnEntity(new ItemEntity
            {
                Level = this,
                EntityId = Server.GetNextEntityId(),
                Position = (VectorD)position + new VectorD(0.5, 0.5, 0.5),
                Item = remainder
            });
        }

        if (step.Bottles is bool[] bottles && GameEventBlock(position) is IBlock block)
        {
            var shown = block;
            for (var slot = 0; slot < bottles.Length; slot++)
                shown = shown.WithProperty(bottleProperties[slot], bottles[slot]);

            if (!shown.Equals(block))
                await SetBlockAsync(position, shown);
        }

        if (step.Slots)
            await MainEventHandler.RefreshViewersAsync(stand);

        if (step.Properties)
        {
            foreach (var viewer in stand.GetViewers())
            {
                if (ReferenceEquals(viewer.OpenedContainer, stand))
                    await SendBrewingPropertiesAsync(viewer, stand);
            }
        }
    }

    /// <summary>
    /// Sends <paramref name="viewer"/> the brew time and fuel of the stand they have open, which its menu's arrow and
    /// fuel bar show (vanilla's brewing menu data).
    /// </summary>
    internal static async ValueTask SendBrewingPropertiesAsync(IPlayer viewer, BrewingStand stand)
    {
        var containerId = viewer.CurrentContainerId;

        await viewer.QueuePacketAsync(new ContainerSetDataPacket
        {
            ContainerId = containerId,
            WindowProperty = new BrewingStandWindowProperty(BrewingStandProperty.BrewTime, (short)stand.BrewTime)
        });

        await viewer.QueuePacketAsync(new ContainerSetDataPacket
        {
            ContainerId = containerId,
            WindowProperty = new BrewingStandWindowProperty(BrewingStandProperty.FuelTime, (short)stand.Fuel)
        });
    }
}
