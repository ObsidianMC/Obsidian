using Microsoft.Extensions.Logging;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Could not evaluate explosion loot for {Block} at {Position}")]
        public static partial void ExplosionLootFailed(
            ILogger logger, Exception exception, string block, Vector position);
    }

    private static readonly Dictionary<string, float> blastResistance = LoadBlastResistance();

    private static Dictionary<string, float> LoadBlastResistance()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Obsidian.Assets.blast_resistance.json")
            ?? throw new InvalidDataException("Missing block blast resistance data.");
        return JsonSerializer.Deserialize<Dictionary<string, float>>(stream)!;
    }

    /// <summary>
    /// An explosion at <paramref name="source"/>, which <paramref name="owner"/> caused if it has one. By default it's
    /// a mob's, like a creeper's or a fireball's; <paramref name="interaction"/> says how else it breaks blocks.
    /// </summary>
    internal ValueTask ExplodeAsync(
        Entity source,
        float radius,
        IEntity? owner = null,
        ExplosionInteraction interaction = ExplosionInteraction.Mob) =>
        ExplodeAsync(source.Position, radius, source, owner, fire: false, interaction);

    /// <summary>
    /// An explosion without an entity causing it, like a bed's or a respawn anchor's where they don't work. With
    /// <paramref name="fire"/>, like vanilla's, a third of the air it reaches above a solid block catches fire.
    /// </summary>
    internal ValueTask ExplodeAsync(VectorD center, float radius, bool fire, ExplosionInteraction interaction) =>
        ExplodeAsync(center, radius, source: null, owner: null, fire, interaction);

    private async ValueTask ExplodeAsync(
        VectorD center,
        float radius,
        Entity? source,
        IEntity? owner,
        bool fire,
        ExplosionInteraction interaction)
    {
        // Capture the caller once, before awaited damage callbacks, rather than walking for each block drop.
        var spawner = PluginEntities.FindSpawner();
        var random = source is Mob mob ? mob.Random : Random.Shared;
        var terrain = new MobTerrain(this);
        var destroyed = new HashSet<Vector>();
        var reached = new HashSet<Vector>();
        for (var x = 0; x < 16; x++)
        for (var y = 0; y < 16; y++)
        for (var z = 0; z < 16; z++)
        {
            if (x is not 0 and not 15 && y is not 0 and not 15 && z is not 0 and not 15)
                continue;
            var direction = new VectorD(x / 15f * 2 - 1, y / 15f * 2 - 1, z / 15f * 2 - 1);
            direction /= direction.Magnitude;
            var point = center;
            var strength = radius * (0.7f + random.NextSingle() * 0.6f);
            while (strength > 0)
            {
                var position = (Vector)point.Floor();
                var block = terrain.GetBlock(position);
                if (block == null)
                    break;
                if (!block.IsAir)
                {
                    var resistance = BlocksRegistry.TryGetPluginBlock(block.GetHashCode(), out var pluginBlock)
                        ? pluginBlock.Settings.ExplosionResistance
                        : blastResistance[block.UnlocalizedName];
                    if (source is WitherSkull { Invulnerable: true }
                        && !TagsRegistry.Block.WitherImmune.Entries.Contains(block.RegistryId))
                    {
                        resistance = Math.Min(resistance, 0.8f);
                    }

                    strength -= (resistance + 0.3f) * 0.3f;
                }
                if (strength > 0 && !block.IsAir)
                    destroyed.Add(position);

                if (strength > 0 && fire)
                    reached.Add(position);
                point += direction * 0.3f;
                strength -= 0.22500001f;
            }
        }
        var knockbacks = new Dictionary<int, VectorD>();
        var diameter = radius * 2;
        var targets = GetEntitiesInRange(center, diameter + 1)
            .OfType<Entity>()
            .Where(entity => entity is Living or EndCrystal)
            .ToArray();

        foreach (var target in targets)
        {
            if (ReferenceEquals(target, source) || target.Health <= 0 ||
                target is IPlayer player && player.GameMode == GameMode.Spectator)
                continue;
            var distance = (target.Position - center).Magnitude / diameter;
            if (distance > 1)
                continue;
            var direction = target.Position + new VectorD(0, target.Dimension.Height * 0.85f, 0) - center;
            if (direction.Magnitude < 0.000001f)
                continue;
            direction /= direction.Magnitude;
            var bounds = target.Dimension.CreateBBFromPosition(target.Position);
            var total = 0;
            var visible = 0;
            var steps = new VectorD(
                (float)(1 / (bounds.Width * 2 + 1)),
                (float)(1 / (bounds.Height * 2 + 1)),
                (float)(1 / (bounds.Depth * 2 + 1)));
            for (var a = 0d; a <= 1; a += steps.X)
            for (var b = 0d; b <= 1; b += steps.Y)
            for (var c = 0d; c <= 1; c += steps.Z)
            {
                total++;
                var sample = bounds.Min + (bounds.Max - bounds.Min) * new VectorD(a, b, c);
                if (terrain.HasLineOfSight(sample, center))
                    visible++;
            }
            var exposure = (1 - distance) * visible / Math.Max(1, total);
            var damage = (float)Math.Floor((exposure * exposure + exposure) / 2 * 7 * diameter + 1);
            // Without an entity behind it, the victim stands in as the attacker the damage handling needs; the centre
            // is where the damage comes from either way (an explosion's source position in vanilla), which shields
            // block by.
            var attacker = owner ?? source ?? (IEntity)target;
            if (target is Living living)
                await living.DamageCombatAsync(attacker, damage, CombatDamageKind.Explosion, sourcePosition: center);
            else
                await target.DamageAsync(attacker, damage);
            var knockback = direction * exposure
                * (1 - Math.Clamp(target.GetAttributeValue("minecraft:generic.knockback_resistance"), 0, 1));
            target.Motion += knockback;
            if (target is IPlayer)
                knockbacks[target.EntityId] = knockback;
        }
        // Vanilla's ServerLevel.explode: which blocks break, and which rule decays their drops, depend on what kind of
        // explosion it is. Mobs' break nothing without mob_griefing.
        var keepsBlocks = interaction == ExplosionInteraction.None
            || interaction == ExplosionInteraction.Mob && !LevelData.GetBooleanRule("mob_griefing");

        if (keepsBlocks)
            destroyed.Clear();

        var dropDecay = LevelData.GetBooleanRule(interaction == ExplosionInteraction.Block
            ? "block_explosion_drop_decay"
            : "mob_explosion_drop_decay");

        var removed = new HashSet<Vector>();
        foreach (var position in destroyed)
        {
            if (!removed.Add(position))
                continue;

            var block = terrain.GetBlock(position)!;
            var parts = await TwoPartBlockBreak.PrepareAsync(this, position, block);
            List<ItemStack> drops;
            try
            {
                // Block entity integration supplies components/dynamic contents here, from parts.DropPosition.
                drops = await BlockLoot.GetDropsAsync(this, parts.DropPosition, parts.DropState, null,
                    breaker: source, explosionRadius: dropDecay ? radius : null, checkTool: false);
            }
            catch (Exception ex)
            {
                Log.ExplosionLootFailed(Logger, ex, block.UnlocalizedName, position);
                drops = [];
            }

            await parts.RemovePartnerAsync(this);
            if (parts.PartnerPosition is Vector partnerPosition)
                removed.Add(partnerPosition);

            await SetBlockAsync(position, BlocksRegistry.Air, true);
            foreach (var item in drops)
            {
                var drop = new ItemEntity
                {
                    Level = this,
                    EntityId = Server.GetNextEntityId(),
                    Position = (VectorD)parts.DropPosition + 0.5f,
                    Item = item,
                    SpawnedBy = spawner
                };

                if (!this.TrySpawnEntity(drop, hostOwned: true))
                    throw new InvalidOperationException($"Could not register entity {drop.EntityId}.");
            }
        }
        // Vanilla's ServerExplosion.createFire: each position reached, now air, catches fire a third of the time
        // when the block below is solid.
        foreach (var position in reached)
        {
            if (random.Next(3) == 0
                && terrain.GetBlock(position)?.IsAir == true
                && terrain.GetBlock(position + Vector.Down) is IBlock below
                && below.IsSolid())
            {
                var flame = below.RegistryId is BlockIds.SoulSand or BlockIds.SoulSoil
                    ? BlocksRegistry.SoulFire
                    : BlocksRegistry.Fire;

                await SetBlockAsync(position, flame, true);
            }
        }

        var sound = new SoundEffect
        {
            SoundId = "minecraft:entity.generic.explode",
            SoundCategory = SoundCategory.Blocks,
            Volume = 4,
            Pitch = 1,
            Seed = random.NextInt64()
        };
        foreach (var player in GetPlayersInRange(center, 64))
        {
            var knockback = knockbacks.GetValueOrDefault(player.EntityId);
            await player.QueuePacketAsync(new ExplodePacket
            {
                Center = center,
                Radius = radius,
                BlockCount = destroyed.Count,
                PlayerKnockback = new Velocity(knockback.X, knockback.Y, knockback.Z),
                ExplosionParticle = new ExplosionParticleData(),
                ExplosionSound = sound,
                ExplosionParticleInfo = []
            });
        }
    }

    /// <summary>
    /// How an explosion breaks blocks, like vanilla's <c>Level.ExplosionInteraction</c>: the kinds of explosion
    /// Obsidian has.
    /// </summary>
    internal enum ExplosionInteraction
    {
        /// <summary>Breaks nothing, like an end crystal's at the end of the dragon's resurrection.</summary>
        None,

        /// <summary>A block's or an end crystal's: <c>block_explosion_drop_decay</c> decays its drops.</summary>
        Block,

        /// <summary>
        /// A mob's, or its projectile's: breaks blocks only with <c>mob_griefing</c>, and
        /// <c>mob_explosion_drop_decay</c> decays its drops.
        /// </summary>
        Mob
    }

    private sealed class ExplosionParticleData : ParticleData
    {
        public override ParticleType ParticleType => ParticleType.ExplosionEmitter;
    }
}
