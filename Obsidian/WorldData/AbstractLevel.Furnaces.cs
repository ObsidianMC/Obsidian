using Obsidian.API.Inventory;
using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.WindowProperties;
using Obsidian.WorldData.Furnaces;

namespace Obsidian.WorldData;

/// <summary>
/// Ticks furnaces, blast furnaces and smokers like vanilla <c>AbstractFurnaceBlockEntity.serverTick</c>. Only furnaces that
/// might change are tracked: those in a loaded chunk, or open to a player. An idle furnace with no viewers is dropped.
/// </summary>
public abstract partial class AbstractLevel
{
    private readonly ConcurrentDictionary<Vector, byte> activeFurnaces = [];

    /// <summary>Starts ticking the furnace at <paramref name="point"/>, e.g. when a player opens it.</summary>
    internal void TrackFurnace(Vector point) => activeFurnaces[point] = 0;

    private void RegisterChunkFurnaces(Chunk chunk)
    {
        foreach (var entity in chunk.GetBlockEntities())
        {
            if (entity is SmeltingContainer furnace)
                TrackFurnace(furnace.BlockPosition);
        }
    }

    private async ValueTask TickFurnacesAsync()
    {
        foreach (var point in activeFurnaces.Keys.ToArray())
        {
            TickStage = $"furnace at {point}";
            await TickFurnaceAsync(point);
        }
    }

    private async ValueTask TickFurnaceAsync(Vector point)
    {
        var chunk = GetLoadedChunk(point.X >> 4, point.Z >> 4);
        if (chunk?.GetBlockEntity(point.X, point.Y, point.Z) is not SmeltingContainer furnace)
        {
            activeFurnaces.TryRemove(point, out _);
            return;
        }

        var block = GameEventBlock(point);
        if (block?.BlockEntityKind() is not (BlockEntityType.Furnace or BlockEntityType.BlastFurnace or BlockEntityType.Smoker))
        {
            activeFurnaces.TryRemove(point, out _);
            return;
        }

        var kind = FurnaceRules.KindOf(furnace.Type);

        var wasLit = furnace.FuelBurnTime > 0;
        var changed = false;
        if (furnace.FuelBurnTime > 0)
            furnace.FuelBurnTime--;

        var input = furnace.GetItem(0);
        var recipe = FurnaceRules.FindRecipe(kind, input);
        var output = recipe?.Result.FirstOrDefault();
        var canSmelt = output is not null && CanAcceptOutput(furnace.GetResult(), output);

        // Vanilla lights a new fuel only when there's something to smelt.
        if (furnace.FuelBurnTime == 0 && canSmelt)
        {
            var fuel = furnace.GetItem(1);
            var burnTime = FurnaceRules.GetBurnTime(fuel);
            if (burnTime > 0)
            {
                furnace.FuelBurnTotal = (short)burnTime;
                furnace.FuelBurnTime = (short)burnTime;
                changed = true;
                ConsumeFuel(furnace, fuel);
            }
        }

        if (furnace.FuelBurnTime > 0 && canSmelt)
        {
            furnace.CookTimeTotal = (short)(recipe?.CookingTime ?? FurnaceRules.DefaultCookTime);
            furnace.CookTime++;
            if (furnace.CookTime >= furnace.CookTimeTotal)
            {
                furnace.CookTime = 0;
                ConsumeInput(furnace);
                AddOutput(furnace, output!);
                changed = true;
            }
        }
        else if (furnace.CookTime > 0)
        {
            // Vanilla cools the progress down by 2 a tick instead of resetting it.
            furnace.CookTime = (short)Math.Max(0, furnace.CookTime - 2);
        }

        var isLit = furnace.FuelBurnTime > 0;
        if (isLit != wasLit)
        {
            await SetBlockAsync(point, block.WithProperty("lit", isLit));
        }

        // Slots resync only when they changed; progress bars update each tick while someone has the furnace open.
        await SendFurnaceStateAsync(furnace, changed);

        // An idle furnace stays tracked while a player has it open, so inserting fuel or input still gets smelted.
        if (furnace.FuelBurnTime == 0 && furnace.CookTime == 0 && furnace.Viewers.Count == 0)
            activeFurnaces.TryRemove(point, out _);
    }

    private static bool CanAcceptOutput(ItemStack? current, ItemStack output)
    {
        if (current is null || current.IsAir)
            return true;

        return current == output && current.Count + output.Count <= current.MaxStackSize;
    }

    private static void ConsumeFuel(SmeltingContainer furnace, ItemStack fuel)
    {
        // A lava bucket leaves an empty bucket behind, like vanilla's craftingRemainingItem.
        if (fuel.Type == Material.LavaBucket)
        {
            furnace.SetItem(1, ItemsRegistry.GetSingleItem(Material.Bucket));
            return;
        }

        var remaining = fuel;
        remaining -= 1;
        furnace.SetItem(1, remaining.Count > 0 ? remaining : null);
    }

    private static void ConsumeInput(SmeltingContainer furnace)
    {
        var input = furnace.GetItem(0);
        if (input is null || input.IsAir)
            return;

        var remaining = input;
        remaining -= 1;
        furnace.SetItem(0, remaining.Count > 0 ? remaining : null);
    }

    private static void AddOutput(SmeltingContainer furnace, ItemStack output)
    {
        var current = furnace.GetResult();
        if (current is null || current.IsAir)
        {
            furnace.SetResult(new ItemStack(output, output.Count));
            return;
        }

        var result = current;
        result += output.Count;
        furnace.SetResult(result);
    }

    /// <summary>
    /// Sends the furnace's slots and progress bars to each player who has it open. Slots are sent with the player's
    /// inventory tail, like the click handler's resync; progress uses <c>ContainerSetData</c>.
    /// </summary>
    internal static async ValueTask SendFurnaceStateAsync(SmeltingContainer furnace, bool includeSlots)
    {
        foreach (var viewer in furnace.Viewers.ToArray())
        {
            if (viewer is not Player player)
                continue;

            var containerId = player.CurrentContainerId;
            if (includeSlots)
            {
                var contents = furnace.ToList();
                contents.AddRange(player.Inventory.Skip(9).Take(36));
                await player.Client.QueuePacketAsync(new ContainerSetContentPacket(containerId, contents));
            }

            await player.Client.QueuePacketAsync(new ContainerSetDataPacket { ContainerId = containerId, WindowProperty = new FurnaceWindowProperty(FurnaceProperty.FuelLeft, furnace.FuelBurnTime) });
            await player.Client.QueuePacketAsync(new ContainerSetDataPacket { ContainerId = containerId, WindowProperty = new FurnaceWindowProperty(FurnaceProperty.MaximumFuelBurnTime, furnace.FuelBurnTotal) });
            await player.Client.QueuePacketAsync(new ContainerSetDataPacket { ContainerId = containerId, WindowProperty = new FurnaceWindowProperty(FurnaceProperty.ProgressArrow, furnace.CookTime) });
            await player.Client.QueuePacketAsync(new ContainerSetDataPacket { ContainerId = containerId, WindowProperty = new FurnaceWindowProperty(FurnaceProperty.MaximumProgress, furnace.CookTimeTotal) });
        }
    }
}
