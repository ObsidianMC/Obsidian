using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:experience_orb")]
public sealed partial class ExperienceOrb : Entity
{
    private int age;
    private VectorF motion = new((Globals.Random.NextSingle() * 2 - 1) * 0.2f,
        Globals.Random.NextSingle() * 0.4f, (Globals.Random.NextSingle() * 2 - 1) * 0.2f);
    public int Value { get; init; }

    public ExperienceOrb() => Type = EntityType.ExperienceOrb;

    public static int SplitValue(int remaining) => remaining switch
    {
        >= 2477 => 2477, >= 1237 => 1237, >= 617 => 617, >= 307 => 307, >= 149 => 149,
        >= 73 => 73, >= 37 => 37, >= 17 => 17, >= 7 => 7, >= 3 => 3, _ => 1
    };

    public override async ValueTask TickAsync()
    {
        if (Level is not AbstractLevel level || level.GetLoadedChunk((int)MathF.Floor(Position.X) >> 4,
            (int)MathF.Floor(Position.Z) >> 4) == null)
            return;
        if (++age >= 6000)
        {
            await RemoveAsync();
            return;
        }
        var player = Level.GetPlayersInRange(Position, 8).OfType<Player>()
            .Where(candidate => candidate.Alive && candidate.Gamemode != Gamemode.Spectator)
            .OrderBy(candidate => (candidate.Position - Position).MagnitudeSquared()).FirstOrDefault();
        if (player != null)
        {
            var difference = player.Position + new VectorF(0, (float)(player.HeadY - player.Position.Y) / 2, 0) - Position;
            var distance = difference.Magnitude;
            if (distance is > 0 and < 8)
                motion += difference / distance * (MathF.Pow(1 - distance / 8, 2) * 0.1f);
            if ((player.Position - Position).MagnitudeSquared() < 1 &&
                Level.LevelData.Time - player.LastExperiencePickupTick >= 2)
            {
                player.LastExperiencePickupTick = Level.LevelData.Time;
                player.XpTotal += Value;
                var points = player.XpP * XpHelper.ExperienceRequiredForNextLevel(player.XpLevel) + Value;
                while (points >= XpHelper.ExperienceRequiredForNextLevel(player.XpLevel))
                {
                    points -= XpHelper.ExperienceRequiredForNextLevel(player.XpLevel);
                    player.XpLevel++;
                }
                player.XpP = points / XpHelper.ExperienceRequiredForNextLevel(player.XpLevel);
                await player.Client.QueuePacketAsync(new SetExperiencePacket(player.XpP, player.XpLevel, player.XpTotal));
                await RemoveAsync();
                return;
            }
        }
        var terrain = new MobTerrain(Level);
        motion.Y -= 0.03f;
        var bounds = Dimension.CreateBBFromPosition(Position);
        var swept = new BoundingBox(VectorF.Min(bounds.Min, bounds.Min + motion), VectorF.Max(bounds.Max, bounds.Max + motion));
        var delta = EntityMovement.Clip(bounds, motion, terrain.GetCollisions(swept).ToArray());
        var grounded = motion.Y < 0 && delta.Y != motion.Y;
        var next = Position + delta;
        if (!level.TryMoveEntity(this, Position, next))
            return;
        await UpdateAsync(next, grounded ? MovementFlags.OnGround : MovementFlags.None);
        var friction = grounded ? terrain.GetFriction(next) * 0.98f : 0.98f;
        motion = new VectorF(delta.X == motion.X ? motion.X * friction : 0,
            grounded ? -motion.Y * 0.9f : delta.Y == motion.Y ? motion.Y * 0.98f : 0,
            delta.Z == motion.Z ? motion.Z * friction : 0);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.VarInt);
        writer.WriteVarInt(Value);
    }
}
