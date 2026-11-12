using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    /// <summary>The player/ominous wind charge's nondamaging, triggering explosion, distinct from a breeze burst.</summary>
    internal async ValueTask BurstWindChargeAsync(Entity source, VectorD center)
    {
        const float power = 1.2f;
        const float influence = power * 2;
        const float multiplier = 1.22f;
        var terrain = new MobTerrain(this);
        this.EmitGameEvent(MobGameEvent.Explode, center, source);
        var blocks = this.WindChargeBlocks(terrain, center, power);
        var pushed = new Dictionary<int, VectorD>();

        foreach (var entity in this.GetEntitiesInRange(center, influence + 1).OfType<Entity>())
        {
            if (ReferenceEquals(entity, source) || !entity.IsActive || entity is BreezeWindCharge
                || entity is Creaking { CanMove: false }
                || entity is IPlayer flying && flying.Abilities.HasFlag(PlayerAbility.Flying))
            {
                continue;
            }

            var distance = (entity.Position - center).Magnitude / influence;
            if (distance > 1)
                continue;

            var eyes = entity switch
            {
                PrimedTnt => entity.Position,
                IPlayer player => PlayerBase.EyePosition(player),
                Mob mob => mob.EyePosition,
                _ => entity.Position + new VectorD(0, entity.Dimension.Height * 0.85f, 0)
            };
            var direction = eyes - center;
            if (direction.Magnitude < 1e-12)
                continue;

            direction /= direction.Magnitude;
            var exposure = WindChargeExposure(terrain, center, entity.Dimension.CreateBBFromPosition(entity.Position));
            var resistance = entity is Living
                ? Math.Clamp(entity.GetAttributeValue("minecraft:explosion_knockback_resistance"), 0, 1)
                : 0;
            var knockback = direction * ((1 - distance) * exposure * multiplier * (1 - resistance));
            entity.Motion += knockback;
            if (entity is IPlayer)
                pushed[entity.EntityId] = knockback;
        }

        // Native explosions shuffle the reached cells, including air, before their trigger callbacks. This also
        // preserves the level-random draws even when none of those cells contains a reactive block.
        for (var count = blocks.Count; count > 1; count--)
        {
            var index = this.GrowthRandom.NextInt(count);
            (blocks[count - 1], blocks[index]) = (blocks[index], blocks[count - 1]);
        }
        foreach (var position in blocks)
            await this.TriggerWindChargeBlockAsync(position);

        var sound = new SoundEffect
        {
            SoundId = "minecraft:" + SoundId.Entity.WindChargeWindBurst,
            SoundCategory = SoundCategory.Blocks,
            Volume = 1,
            Pitch = 1,
            // ExplodePacket carries only the sound holder; there is no sound seed on this wire format.
            Seed = 0
        };
        foreach (var player in this.GetPlayersInRange(center, 64))
        {
            Velocity? knockback = pushed.TryGetValue(player.EntityId, out var motion)
                ? new Velocity(motion.X, motion.Y, motion.Z)
                : null;
            await player.QueuePacketAsync(new ExplodePacket
            {
                Center = center,
                Radius = power,
                BlockCount = blocks.Count,
                PlayerKnockback = knockback,
                ExplosionParticle = new WindChargeParticle(),
                ExplosionSound = sound,
                ExplosionParticleInfo = []
            });
        }
    }

    private sealed class WindChargeParticle : ParticleData
    {
        public override ParticleType ParticleType => ParticleType.GustEmitterSmall;
    }

    private List<Vector> WindChargeBlocks(MobTerrain terrain, VectorD center, float power)
    {
        const float step = 0.3f;
        var reached = new HashSet<Vector>();
        for (var x = 0; x < 16; x++)
        {
            for (var y = 0; y < 16; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    if (x is not 0 and not 15 && y is not 0 and not 15 && z is not 0 and not 15)
                        continue;

                    var direction = new VectorD(x / 15f * 2 - 1, y / 15f * 2 - 1, z / 15f * 2 - 1);
                    direction /= direction.Magnitude;
                    var point = center;
                    var remaining = power * (0.7f + this.GrowthRandom.NextFloat() * 0.6f);
                    while (remaining > 0)
                    {
                        var position = (Vector)point.Floor();
                        var block = terrain.GetBlock(position);
                        if (block is null || this.IsOutsideBuildHeight(position.Y))
                            break;

                        if (TagsRegistry.Block.BlocksWindChargeExplosions.Entries.Contains(block.RegistryId))
                            break;

                        reached.Add(position);
                        point += direction * step;
                        remaining -= 0.22500001f;
                    }
                }
            }
        }

        // BlockPos's hash-set iteration order determines which cell receives each native shuffle draw.
        var capacity = 16;
        while (reached.Count > capacity * 3 / 4)
            capacity *= 2;

        return reached.OrderBy(position =>
        {
            var hash = unchecked((position.Y + position.Z * 31) * 31 + position.X);
            return (hash ^ (int)((uint)hash >> 16)) & (capacity - 1);
        }).ToList();
    }

    private static float WindChargeExposure(MobTerrain terrain, VectorD center, BoundingBox bounds)
    {
        var step = new VectorD(1 / (bounds.Width * 2 + 1), 1 / (bounds.Height * 2 + 1), 1 / (bounds.Depth * 2 + 1));
        var offset = new VectorD((1 - Math.Floor(1 / step.X) * step.X) / 2, 0,
            (1 - Math.Floor(1 / step.Z) * step.Z) / 2);
        var total = 0;
        var visible = 0;
        for (var x = 0d; x <= 1; x += step.X)
        {
            for (var y = 0d; y <= 1; y += step.Y)
            {
                for (var z = 0d; z <= 1; z += step.Z)
                {
                    total++;
                    var sample = bounds.Min + (bounds.Max - bounds.Min) * new VectorD(x, y, z) + offset;
                    if (terrain.HasLineOfSight(sample, center))
                        visible++;
                }
            }
        }
        return (float)visible / Math.Max(1, total);
    }

    private async ValueTask TriggerWindChargeBlockAsync(Vector position)
    {
        var block = this.BlockUpdates.GetBlock(position);
        if (block.RegistryId is BlockIds.Fire or BlockIds.SoulFire)
        {
            await this.SetBlockAsync(position, BlocksRegistry.Air, true);
            return;
        }

        var kind = block.BlockClass();
        var door = kind is "DoorBlock" or "WeatheringCopperDoorBlock";
        var trapdoor = kind is "TrapDoorBlock" or "WeatheringCopperTrapDoorBlock";
        if (door || trapdoor || kind == "FenceGateBlock")
        {
            // Native explosion callbacks leave powered openables alone and only address a door's lower half.
            // All block sets except iron permit wind charges, including copper and gold.
            if (block.GetProperty("powered") == "true" || door && block.GetProperty("half") != "lower"
                || block.RegistryId is BlockIds.IronDoor or BlockIds.IronTrapdoor)
            {
                return;
            }

            var open = block.GetProperty("open") != "true";
            var flags = BlockUpdateFlags.All;
            if (door)
                flags = BlockUpdateFlags.Clients | BlockUpdateFlags.Immediate;
            else if (trapdoor)
                flags = BlockUpdateFlags.Clients;

            await this.WriteAsync(position, block.WithProperty("open", open), flags);
            if (trapdoor && block.GetProperty("waterlogged") == "true")
                this.Fluids.ScheduleShapeFluidTick(position);

            ((IRedstoneLevel)this).PlaySound(position, VanillaPlacement.OpenSound(block, open), 1,
                this.GrowthRandom.NextFloat() * 0.1f + 0.9f);
            this.EmitGameEvent(open ? MobGameEvent.BlockOpen : MobGameEvent.BlockClose, position, null);
        }
        else if (await this.TriggerRedstoneAsync(position))
        {
            return;
        }
        else if (block.RegistryId == BlockIds.Bell && ((IRedstoneLevel)this).GetBlockEntityAt(position) is not null)
        {
            var facing = FeatureHelpers.ParseFace(block.GetProperty("facing"));
            this.QueueBlockEvent(position, block.RegistryId, RedstoneBell.RingEvent, (int)facing);
            ((IRedstoneLevel)this).PlaySound(position, RedstoneBell.RingSound, RedstoneBell.RingVolume);
            this.EmitGameEvent(MobGameEvent.BlockChange, position, null);
        }
        else if (block.GetProperty("lit") == "true"
            && (block.RegistryId is BlockIds.Campfire or BlockIds.SoulCampfire
                || TagsRegistry.Block.Candles.Entries.Contains(block.RegistryId)))
        {
            await this.SetBlockAsync(position, block.WithProperty("lit", false), true);
        }
    }
}
