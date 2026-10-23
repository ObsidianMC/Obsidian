using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:experience_orb")]
public sealed partial class ExperienceOrb : Entity
{
    private int age;
    private VectorD motion = new((Globals.Random.NextSingle() * 2 - 1) * 0.2f,
        Globals.Random.NextSingle() * 0.4f, (Globals.Random.NextSingle() * 2 - 1) * 0.2f);
    public int Value { get; init; }

    // Kept across ticks, as mobs keep theirs, rather than made (with its chunk cache) every tick; made again when the
    // entity changes level.
    private MobTerrain? terrain;
    private ILevel? terrainLevel;

    // The collision shapes of the tick's move, reused like EntityMovement.Collide's.
    [ThreadStatic] private static List<BoundingBox>? collisionShapes;

    public ExperienceOrb() => Type = EntityType.ExperienceOrb;

    public static int SplitValue(int remaining) => remaining switch
    {
        >= 2477 => 2477, >= 1237 => 1237, >= 617 => 617, >= 307 => 307, >= 149 => 149,
        >= 73 => 73, >= 37 => 37, >= 17 => 17, >= 7 => 7, >= 3 => 3, _ => 1
    };

    public override async ValueTask TickAsync()
    {
        if (Level is not AbstractLevel level || level.GetLoadedChunk((int)Math.Floor(Position.X) >> 4,
            (int)Math.Floor(Position.Z) >> 4) == null)
            return;
        if (++age >= 6000)
        {
            await RemoveAsync();
            return;
        }
        var bounds = Dimension.CreateBBFromPosition(Position);
        var player = Level.GetPlayersInRange(Position, 8)
            .Where(candidate => candidate.Alive && candidate.GameMode != GameMode.Spectator)
            .OrderBy(candidate => (candidate.Position - Position).MagnitudeSquared()).FirstOrDefault();
        if (player != null)
        {
            var difference = player.Position + new VectorD(0, (float)(player.HeadY - player.Position.Y) / 2, 0) - Position;
            var distance = difference.Magnitude;
            if (distance is > 0 and < 8)
                motion += difference / distance * (Math.Pow(1 - distance / 8, 2) * 0.1f);
            // Vanilla: the orb's box must overlap the player's pickup area (the player's box grown by 1 horizontally and 0.5 vertically).
            if (bounds.Intersects(PlayerBase.PickupAreaOf(player)) && await player.PickUpExperienceAsync(Value))
            {
                await RemoveAsync();
                return;
            }
        }
        if (terrain is null || !ReferenceEquals(terrainLevel, Level))
        {
            terrain = new MobTerrain(Level);
            terrainLevel = Level;
        }
        motion.Y -= 0.03f;
        var swept = new BoundingBox(VectorD.Min(bounds.Min, bounds.Min + motion), VectorD.Max(bounds.Max, bounds.Max + motion));
        var shapes = collisionShapes ??= [];
        shapes.Clear();
        terrain.CollectCollisions(swept, shapes);
        var delta = EntityMovement.Clip(bounds, motion, shapes);
        var grounded = motion.Y < 0 && delta.Y != motion.Y;
        var next = Position + delta;
        if (!level.TryMoveEntity(this, Position, next))
            return;
        await UpdateAsync(next, grounded ? MovementFlags.OnGround : MovementFlags.None);
        var friction = grounded ? terrain.GetFriction(next) * 0.98f : 0.98f;
        motion = new VectorD(delta.X == motion.X ? motion.X * friction : 0,
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
