using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.WorldData.Fluids;
using Obsidian.WorldData.Generators.Mojang;
using System.Threading;

namespace Obsidian.WorldData;

internal sealed class PotentSulfurBlockEntity : BlockEntity
{
    private NbtCompound retained = new();
    private int countdown = -1;

    internal override bool SavesOnAnyThread => true;

    internal int Countdown
    {
        get => Volatile.Read(ref this.countdown);
        set => Volatile.Write(ref this.countdown, value);
    }

    protected internal override void Load(NbtCompound tag)
    {
        var copy = DataBlockEntity.Copy(tag);
        this.Countdown = copy.TryGetNumber<int>("countdown", out var value) ? value : -1;
        copy.Remove("countdown");
        Volatile.Write(ref this.retained, copy);
    }

    protected internal override void Save(NbtCompound tag)
    {
        foreach (var (name, value) in DataBlockEntity.Copy(Volatile.Read(ref this.retained)))
        {
            tag.Add(name, value);
        }

        tag.Add(new NbtTag<int>("countdown", this.Countdown));
    }

    protected internal override ValueTask TickAsync()
    {
        if (this.Level is AbstractLevel level)
        {
            lock (level.Fluids.TickLock)
            {
                this.Tick(level);
            }
        }

        return ValueTask.CompletedTask;
    }

    internal void Tick(AbstractLevel level)
    {
        var state = level.BlockUpdates.GetBlock(this.BlockPosition);
        var mode = state.GetProperty("potent_sulfur_state");
        if (mode == "dry" || SulfurGas.FindSource(level.BlockUpdates, this.BlockPosition) is not Vector source)
            return;

        if (mode is "erupting" or "continuous")
        {
            level.LaunchGeyserEntities(this.BlockPosition, source);
        }

        // Matches vanilla's 20-tick countdown, positional seed and discarded eruption draw.
        var time = level.LevelData.GameTime;
        if (mode is "dormant" or "erupting" && time % 20 == 0)
        {
            if (this.Countdown <= 0)
            {
                var seed = WorldSeed.Parse(level.Seed) ^ -904011478L;
                var random = new XoroshiroRandomSource(seed).ForkPositional()
                    .At(this.BlockPosition.X, this.BlockPosition.Y, this.BlockPosition.Z);
                var depth = source.Y - this.BlockPosition.Y - 1;
                if (mode == "dormant")
                {
                    this.Countdown = 10 * (depth - 1) + random.NextIntBetweenInclusive(15, 30);
                }
                else
                {
                    random.NextInt();
                    this.Countdown = depth - 1 + random.NextIntBetweenInclusive(1, 2);
                }
            }

            if (this.Countdown > 0)
            {
                this.Countdown--;
            }

            if (this.Countdown == 0)
            {
                var next = mode == "dormant" ? "erupting" : "dormant";
                level.BlockUpdates.SetBlock(this.BlockPosition, state.WithProperty("potent_sulfur_state", next));
                if (next == "dormant")
                {
                    level.EmitGameEvent(MobGameEvent.BlockDeactivate, (VectorD)this.BlockPosition + 0.5);
                }
            }

            this.SetChanged();
        }

        if (mode is "wet" or "dormant" && time % 10 == 0)
        {
            level.ApplySulfurGas(source);
        }
    }
}

internal static class SulfurGas
{
    internal static bool IsSourceWater(IBlock block) =>
        FluidState.Of(block) is { Kind: FluidKind.Water, IsSource: true };

    internal static bool Passable(IBlockLevel level, Vector position)
    {
        var state = level.GetBlock(position);
        // Vanilla's sulfur position context starts below the sampled block, so scaffolding has no collision here.
        if (state.Is(BlockIds.Scaffolding))
            return true;

        return state.IsAir || state.Is(BlockIds.Water)
            || BlockCollisionShapes.Get(level.Level, position, state).Count == 0;
    }

    internal static Vector? FindSource(IBlockLevel level, Vector origin)
    {
        // Vanilla accepts at most four source-water blocks, including collision-free waterlogged blocks.
        for (var y = 1; y <= 5; y++)
        {
            var position = origin + new Vector(0, y, 0);
            var state = level.GetBlock(position);
            if (IsSourceWater(state) && Passable(level, position))
                continue;

            return Passable(level, position) ? position : null;
        }

        return null;
    }

    internal static int ForceHeight(IBlockLevel level, Vector origin, int depth)
    {
        for (var height = 0; height < 6 * depth; height++)
        {
            if (!Passable(level, origin + new Vector(0, height + 1, 0)))
                return height;
        }

        return 6 * depth;
    }

    internal static bool Reaches(AbstractLevel level, Vector source, VectorD eyes)
    {
        var cell = (Vector)eyes.Floor();
        if (!Passable(level.BlockUpdates, cell)
            || (eyes - ((VectorD)source + 0.5)).MagnitudeSquared() > 9)
        {
            return false;
        }

        var below = eyes - new VectorD(0, 1, 0);
        return IsSourceWater(level.BlockUpdates.GetBlock((Vector)below.Floor()))
            && new MobTerrain(level).HasLineOfSight((VectorD)(source + Vector.Down) + 0.5, below);
    }
}

internal sealed class PotentSulfurBlockBehavior : IShapeUpdateBehavior, IOnPlaceBehavior, IBlockBehavior
{
    internal static IBlock Validate(IBlockLevel level, Vector position, IBlock state)
    {
        if (!SulfurGas.IsSourceWater(level.GetBlock(position + Vector.Up)))
            return state.WithProperty("potent_sulfur_state", "dry");

        var below = level.GetBlock(position + Vector.Down);
        var fluid = FluidState.Of(below);
        if (fluid.IsEmpty || fluid.IsSource)
        {
            if (TagsRegistry.Block.CausesContinuousGeyserEruptions.Entries.Contains(below.RegistryId))
                return state.WithProperty("potent_sulfur_state", "continuous");

            if (TagsRegistry.Block.CausesPeriodicGeyserEruptions.Entries.Contains(below.RegistryId))
            {
                if (state.GetProperty("potent_sulfur_state") is not ("dormant" or "erupting")
                    && level.GetBlockEntity(position) is PotentSulfurBlockEntity sulfur)
                {
                    sulfur.Countdown = -1;
                }

                return state.GetProperty("potent_sulfur_state") == "erupting"
                    ? state : state.WithProperty("potent_sulfur_state", "dormant");
            }
        }

        return state.WithProperty("potent_sulfur_state", "wet");
    }

    public IBlock? GetStateForPlacement(BlockPlaceContext context) =>
        Validate(context.Level, context.Position, context.Block);

    public IBlock? UpdateShape(
        IBlockLevel level,
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor) => Validate(level, position, state);

    public void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState)
    {
        if (state.GetProperty("potent_sulfur_state") is "erupting" or "continuous"
            && level.Level is AbstractLevel world)
        {
            world.StartGeyser(position, state);
        }
    }
}
