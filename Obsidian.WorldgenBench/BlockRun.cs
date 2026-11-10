using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.Tests;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace WorldgenBench;

/// <summary>
/// An offline, repeatable probe over the same chunks and live update engines as the redstone replays.
/// </summary>
internal static class BlockRun
{
    // Observe lookup results without boxing or allowing the JIT to discard the operation.
    private static object? result;
    private static bool flag;

    public static int Run(ReadOnlySpan<string> args)
    {
        if (!args.IsEmpty)
        {
            Console.Error.WriteLine("Usage: Obsidian.WorldgenBench block (run a Release build)");
            return 2;
        }

        Console.WriteLine($"# {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; "
            + $"{RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine("# 250 ms warmup per case; median of 7 samples; "
            + "delegate/loop overhead included; setup excluded");
        Console.WriteLine("operation,iterations,ns/op,B/op");

        var world = new RedstoneTestWorld();
        world.Load(0, 31, 62, 66, 0, 15);

        var stone = BlocksRegistry.Stone;
        var air = BlocksRegistry.Air;
        var wire = BlocksRegistry.RedstoneWire;

        Measure("IsRedstoneConductor/stone", 2_000_000, () => flag = stone.IsRedstoneConductor());
        Measure("GetTickBehavior/stone", 2_000_000, () => result = BlocksRegistry.GetTickBehavior(stone.RegistryId));
        Measure("BlockClass/stone", 2_000_000, () => result = stone.BlockClass());
        Measure("IsSignalSource/stone", 2_000_000, () => flag = stone.IsSignalSource());
        Measure("IsSignalSource/wire", 2_000_000, () => flag = wire.IsSignalSource());
        Measure("IsGravityAffected/stone", 2_000_000, () => flag = stone.IsGravityAffected());
        Measure("GetProperty/stone-waterlogged", 2_000_000, () => result = stone.GetProperty("waterlogged"));
        Measure("ParseProperty/wire-power", 2_000_000, () =>
        {
            var power = wire.GetProperty("power")!;
            flag = int.Parse(power, CultureInfo.InvariantCulture) != 0;
        });
        Measure("WithProperty/wire-power", 2_000_000, () => result = wire.WithProperty("power", 7));

        Measure("NeighborTryGet/stone", 2_000_000, () => flag = world.Neighbors.TryGet(stone, out _));
        Measure("NeighborTryGet/wire", 2_000_000, () => flag = world.Neighbors.TryGet(wire, out _));
        Measure("RedstoneFor/stone", 2_000_000, () => result = world.Redstone.For(stone));
        Measure("RedstoneFor/wire", 2_000_000, () => result = world.Redstone.For(wire));
        Measure("ContactFlags/air", 2_000_000, () => flag = ContactFlags.Of(air) != 0);
        Measure("BlockEntityType/chest", 2_000_000, () => result = BlocksRegistry.Chest.BlockEntityType());

        var position = new Vector(4, 64, 4);
        var view = new LiveShapeView(world);
        Measure("UpdateShape/stone", 1_000_000,
            () => result = ShapeUpdater.UpdateShape(view, stone, position, BlockFace.West, stone));
        Measure("UpdateShape/air", 1_000_000,
            () => result = ShapeUpdater.UpdateShape(view, air, position, BlockFace.West, stone));

        // The same pass as generation reads it, through a generating level that is no live view.
        var generation = new GenerationLevel(world);
        var fence = BlocksRegistry.OakFence;
        Measure("UpdateShape/generation-stone", 1_000_000,
            () => result = ShapeUpdater.UpdateShape(generation, stone, position, BlockFace.West, stone));
        Measure("UpdateShape/generation-fence", 1_000_000,
            () => result = ShapeUpdater.UpdateShape(generation, fence, position, BlockFace.West, stone));
        var cactus = BlocksRegistry.Cactus;
        Measure("CanSurvive/generation-cactus", 1_000_000,
            () => flag = cactus.CanSurvive(generation, position));

        for (var x = 3; x <= 5; x++)
        {
            for (var y = 63; y <= 65; y++)
            {
                for (var z = 3; z <= 5; z++)
                    world.Commit(new Vector(x, y, z), stone);
            }
        }

        var toggle = false;
        Measure("Pipeline/solid-stone-change", 20_000, () =>
        {
            toggle = !toggle;
            var block = toggle ? BlocksRegistry.Granite : stone;
            var operation = world.Place(position, block);
            Complete(operation);
        });

        DustLine(15);
        DustLine(30);
        LiveLevel();

        BlockEntityPass();
        EntityContacts();

        GC.KeepAlive(result);
        GC.KeepAlive(flag);
        return 0;
    }

    private static void DustLine(int length)
    {
        var world = new RedstoneTestWorld();
        world.Load(-1, length + 1, 62, 66, -1, 1);

        for (var x = 0; x <= length; x++)
            world.Commit(new Vector(x, 63, 0), BlocksRegistry.Stone);

        for (var x = 1; x <= length; x++)
        {
            var position = new Vector(x, 64, 0);
            var operation = world.Place(position, BlocksRegistry.RedstoneWire);
            Complete(operation);
        }

        var source = new Vector(0, 64, 0);
        var powered = false;
        Measure($"Pipeline/toggle-{length}-dust", 500, () =>
        {
            powered = !powered;
            var block = powered ? BlocksRegistry.RedstoneBlock : BlocksRegistry.Air;
            var operation = world.Place(source, block);
            Complete(operation);
        });

        if (world.Pipeline.DroppedWork != 0)
            throw new InvalidOperationException("Dust benchmark exceeded the pipeline work limit.");
    }

    // A level's block entity pass over 25 block-ticking chunks holding 4,000 chests and signs, which don't tick,
    // and an idle furnace and hopper in each chunk, which do. One operation is one level tick's pass.
    private static void BlockEntityPass() => WithTemporaryLevel(MeasureBlockEntityPass);

    // The same changes as the test world cases on a real level, where every block change runs both update engines:
    // the fluid level's vanilla-ordered updater and then the notification pipeline (redstone).
    private static void LiveLevel() => WithTemporaryLevel(MeasureLiveLevel);

    private static void WithTemporaryLevel(Action<World> measure)
    {
        // The level writes region files: keep them out of the working directory, and gone however the case ends.
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-block-probe-{Guid.NewGuid():N}");
        try
        {
            var world = TestWorldFactory.Create("block-probe", folder: folder);
            try
            {
                measure(world);
            }
            finally
            {
                world.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static void MeasureBlockEntityPass(World world)
    {
        var sign = BlocksRegistry.GetDefault(BlockIds.OakSign);

        for (var chunkX = -2; chunkX <= 2; chunkX++)
        {
            for (var chunkZ = -2; chunkZ <= 2; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ);
                for (var index = 0; index < 160; index++)
                {
                    var block = index % 4 == 0 ? sign : BlocksRegistry.Chest;
                    PlaceBlockEntity(chunk, index, block);
                }

                PlaceBlockEntity(chunk, 160, BlocksRegistry.Furnace);
                PlaceBlockEntity(chunk, 161, BlocksRegistry.Hopper);

                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
            }
        }

        // Every chunk ticks: the ticket's ticking square and the ring beyond it.
        using var ticket = world.AddChunkTicket(0, 0, radius: 1);

        Measure("BlockEntityPass/4000-idle-50-ticking", 2_000, () => Complete(world.TickBlockEntitiesAsync()));
    }

    // The contact work of one level tick for 400 armor stands standing on the ground over 25 chunks: everything
    // the entities' contact with blocks costs, but not their own ticks. Once on plain stone with no plates or wire,
    // then with four pressed plates under four of them. The level is a temporary one, deleted afterwards.
    private static void EntityContacts()
    {
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-contact-probe-{Guid.NewGuid():N}");
        try
        {
            var world = TestWorldFactory.Create("contact-probe", folder: folder);
            try
            {
                MeasureEntityContacts(world);
            }
            finally
            {
                world.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static void MeasureEntityContacts(World world)
    {
        for (var chunkX = -2; chunkX <= 2; chunkX++)
        {
            for (var chunkZ = -2; chunkZ <= 2; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ);
                for (var x = 0; x < 16; x++)
                {
                    for (var z = 0; z < 16; z++)
                        chunk.SetBlock((chunkX << 4) + x, 63, (chunkZ << 4) + z, BlocksRegistry.Stone);
                }

                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
            }
        }

        using var ticket = world.AddChunkTicket(0, 0, radius: 1);

        // Two blocks apart, from -20 to 18 on both axes: 400 stands on the ground.
        for (var x = -20; x < 20; x += 2)
        {
            for (var z = -20; z < 20; z += 2)
            {
                var stand = new Obsidian.Entities.ArmorStand
                {
                    Level = world,
                    EntityId = Obsidian.Server.GetNextEntityId(),
                    Position = new VectorD(x + 0.5, 64, z + 0.5),
                    MovementFlags = MovementFlags.OnGround,
                };

                if (!world.TryAddEntity(stand))
                    throw new InvalidOperationException("The probe couldn't add an armor stand.");
            }
        }

        var entities = world.SnapshotEntities().ToArray();
        Measure("EntityContacts/400-on-stone", 200, () => Complete(ContactTickAsync(world, entities)));

        // Pressed already, as the entities on them keep them: a plate that doesn't change is the steady state.
        var plate = BlocksRegistry.GetDefault(BlockIds.OakPressurePlate).WithProperty("powered", true);
        foreach (var (x, z) in new[] { (-20, -20), (-10, 0), (0, 10), (10, -10) })
            world.GetLoadedChunk(x >> 4, z >> 4)!.SetBlock(x, 64, z, plate);

        Measure("EntityContacts/400-on-stone-4-plates", 200, () => Complete(ContactTickAsync(world, entities)));
    }

    // What the level's tick runs for entities touching blocks, apart from the entity loop's own snapshot: the contact
    // pass for each entity after its tick.
    private static async System.Threading.Tasks.ValueTask ContactTickAsync(World world, IEntity[] entities)
    {
        foreach (var entity in entities)
            await world.EntityContacts.TouchAsync((Obsidian.Entities.Entity)entity);
    }

    // Spaced so that no two block entities touch: index 0 to 255 covers the chunk's floor at y 64, two apart.
    private static void PlaceBlockEntity(Chunk chunk, int index, IBlock block)
    {
        var x = (chunk.X << 4) + index % 8 * 2;
        var z = (chunk.Z << 4) + index / 8 % 8 * 2;
        var y = 64 + index / 64 * 2;
        var position = new Vector(x, y, z);

        chunk.SetBlock(x, y, z, block);
        chunk.SetBlockEntity(x, y, z, BlockEntityNbt.Create(block.BlockEntityType()!, position));
    }

    private static void MeasureLiveLevel(World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ);
                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
                Complete(world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false).AsTask());
            }
        }

        // A change inside solid stone, as the test world case above.
        for (var x = 3; x <= 5; x++)
        {
            for (var y = 63; y <= 65; y++)
            {
                for (var z = 3; z <= 5; z++)
                    SetQuietly(world, new Vector(x, y, z), BlocksRegistry.Stone);
            }
        }

        var toggle = false;
        Measure("Level/solid-stone-change", 20_000, () =>
        {
            toggle = !toggle;
            var block = toggle ? BlocksRegistry.Granite : BlocksRegistry.Stone;
            Complete(world.SetBlockUntrackedAsync(4, 64, 4, block, BlockUpdateFlags.All));
        });

        // A 15-dust line on stone, switched by a redstone block at its west end.
        for (var x = -10; x <= 6; x++)
            SetQuietly(world, new Vector(x, 63, -8), BlocksRegistry.Stone);

        for (var x = -9; x <= 5; x++)
            Complete(world.SetBlockUntrackedAsync(x, 64, -8, BlocksRegistry.RedstoneWire, BlockUpdateFlags.All));

        var powered = false;
        Measure("Level/toggle-15-dust", 500, () =>
        {
            powered = !powered;
            var block = powered ? BlocksRegistry.RedstoneBlock : BlocksRegistry.Air;
            Complete(world.SetBlockUntrackedAsync(-10, 64, -8, block, BlockUpdateFlags.All));
        });

        if (world.BlockNotifications.DroppedWork != 0)
            throw new InvalidOperationException("Live dust benchmark exceeded the pipeline work limit.");
    }

    // A block stored without updates, as a case's setup.
    private static void SetQuietly(World world, Vector position, IBlock block) =>
        world.BlockUpdates.SetBlock(position, block, BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);

    private static void Complete(System.Threading.Tasks.Task operation)
    {
        if (!operation.IsCompletedSuccessfully)
            throw new InvalidOperationException("Block benchmark unexpectedly yielded.");
    }

    private static void Complete(System.Threading.Tasks.ValueTask operation)
    {
        // This harness has no asynchronous I/O. Keep allocation accounting on this thread and fail if that changes.
        if (!operation.IsCompletedSuccessfully)
            throw new InvalidOperationException("Block benchmark unexpectedly yielded.");

        operation.GetAwaiter().GetResult();
    }

    private static void Measure(string name, int iterations, Action action)
    {
        var warm = Stopwatch.StartNew();
        do
        {
            for (var i = 0; i < 1_000; i++)
                action();
        }
        while (warm.ElapsedMilliseconds < 250);

        var samples = new (double Nanoseconds, double Bytes)[7];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();

            for (var i = 0; i < iterations; i++)
                action();

            var elapsed = Stopwatch.GetTimestamp() - start;
            samples[sample] = (elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / iterations,
                (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations);
        }

        Array.Sort(samples, (a, b) => a.Nanoseconds.CompareTo(b.Nanoseconds));
        var median = samples[samples.Length / 2];
        var row = FormattableString.Invariant($"{name},{iterations},{median.Nanoseconds:F2},{median.Bytes:F2}");
        Console.WriteLine(row);
    }

    /// <summary>
    /// A generating level over the test world's blocks, as trees and templates hand the shape pass one.
    /// </summary>
    private sealed class GenerationLevel(RedstoneTestWorld world) : IWorldGenLevel
    {
        public long Seed => 0;

        public int MinY => -64;

        public int Height => 384;

        public int SeaLevel => 63;

        public IRandomSource Random => throw new NotSupportedException();

        public IBlock GetBlock(Vector position) => world.GetLoadedBlock(position) ?? BlocksRegistry.VoidAir;

        public bool SetBlock(Vector position, IBlock block) => throw new NotSupportedException();

        public bool EnsureCanWrite(Vector position) => false;

        public int GetHeight(HeightmapType type, int x, int z) => throw new NotSupportedException();

        public BiomeCodec GetBiome(Vector position) => throw new NotSupportedException();

        public void SetBlockEntity(Vector position, IBlockEntity blockEntity) => throw new NotSupportedException();

        public IBlockEntity? GetBlockEntity(Vector position) => null;

        public void AddEntity(GeneratedEntity entity) => throw new NotSupportedException();

        public void MarkForPostProcessing(Vector position) => throw new NotSupportedException();

        public void ScheduleFluidTick(Vector position)
        {
        }
    }
}
