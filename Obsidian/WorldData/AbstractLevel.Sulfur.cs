using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    internal void StartGeyser(Vector position, IBlock state)
    {
        // The block event starts the vanilla client's plume particles and looping sound.
        this.PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)position, new BlockEventPacket
        {
            Position = position,
            BlockType = state.RegistryId,
            ActionId = 0,
            ActionParam = 0
        });

        this.PacketBroadcaster.QueuePacketToLevelInRange(this, (VectorD)position, new SoundPacket
        {
            SoundLocation = state.GetProperty("potent_sulfur_state") == "continuous"
                ? "minecraft:block.potent_sulfur.geyser_continuous_eruption"
                : "minecraft:block.potent_sulfur.geyser_eruption",
            Category = SoundCategory.Blocks,
            SoundPosition = new SoundPosition(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        });

        this.EmitGameEvent(MobGameEvent.BlockActivate, (VectorD)position + 0.5);
    }

    internal void ApplySulfurGas(Vector source)
    {
        var center = (VectorD)source + 0.5;
        var bounds = new BoundingBox(center - new VectorD(3, 0.5, 3), center + new VectorD(3, 0.5, 3));
        foreach (var entity in this.GetEntitiesInRange(center, 8).OfType<Living>())
        {
            if (!SulfurAffected(entity) || !SulfurOverlaps(bounds, entity.BoundingBox))
                continue;

            var eyes = entity is Mob mob ? mob.EyePosition : new VectorD(entity.Position.X,
                entity.Position.Y + entity.Dimension.Height * 0.85, entity.Position.Z);
            if (entity is PlayerBase player)
            {
                eyes = PlayerBase.EyePosition(player);
            }

            if (SulfurGas.Reaches(this, source, eyes))
            {
                // Vanilla applies four seconds of ambient nausea every ten ticks.
                entity.AddPotionEffect((int)PotionEffect.Nausea - 1, 80, effect:
                    EntityEffectFlags.IsAmbient | EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon);
            }
        }
    }

    internal void LaunchGeyserEntities(Vector origin, Vector source)
    {
        var depth = source.Y - origin.Y - 1;
        var height = SulfurGas.ForceHeight(this.BlockUpdates, origin, depth);
        var bounds = new BoundingBox((VectorD)(origin + Vector.Up),
            (VectorD)origin + new VectorD(1, height + 1, 1));
        foreach (var entity in this.GetEntitiesInRange((VectorD)origin, height + 4).OfType<Entity>())
        {
            if (!SulfurAffected(entity) || !SulfurOverlaps(bounds, entity.BoundingBox))
                continue;

            // Matches the float constants and depth-dependent speed threshold of vanilla's geyser ticker.
            entity.CheckGeyserFallDistance();
            if (entity.Vehicle is not null
                || entity is PlayerBase { Abilities: var abilities } && abilities.HasFlag(PlayerAbility.Flying)
                || TagsRegistry.EntityType.NotAffectedByGeysers.Entries.Contains((int)entity.Type)
                || entity.Motion.Y >= (double)0.3f + depth * 0.1)
            {
                continue;
            }

            entity.Motion += new VectorD(0, 0.2f, 0);
            this.PacketBroadcaster.QueuePacketToLevelInRange(this, entity.Position, new SetEntityMotionPacket
            {
                EntityId = entity.EntityId,
                Velocity = new(entity.Motion.X, entity.Motion.Y, entity.Motion.Z)
            });
        }
    }

    private static bool SulfurAffected(Entity entity) =>
        entity is not Living { Alive: false } && entity is not IPlayer { GameMode: GameMode.Spectator };

    private static bool SulfurOverlaps(BoundingBox left, BoundingBox right) =>
        left.Max.X > right.Min.X && left.Min.X < right.Max.X
        && left.Max.Y > right.Min.Y && left.Min.Y < right.Max.Y
        && left.Max.Z > right.Min.Z && left.Min.Z < right.Max.Z;
}
