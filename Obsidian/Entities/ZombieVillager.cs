using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:zombie_villager")]
public sealed partial class ZombieVillager : Zombie
{
    public int ConversionTime { get; private set; } = -1;
    public Guid ConversionStarter { get; private set; }
    public int VillagerXp { get; set; }
    public int VillagerType { get; set; } = 2;
    public int VillagerProfession { get; set; }
    public int VillagerLevel { get; set; } = 1;
    public ZombieVillager() => Type = EntityType.ZombieVillager;
    protected override string? SoundName => "zombie_villager";
    protected override bool CanDespawn => ConversionTime < 0 && VillagerXp == 0;
    internal override async ValueTask InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Health <= 0 || player.Level != Level || player.GameMode == GameMode.Spectator ||
            !IsInRange(player, 4) || !CanSee(player) || ConversionTime >= 0 || !HasPotionEffect((int)PotionEffect.Weakness - 1)) return;
        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (item is not { Count: > 0, Type: Material.GoldenApple }) return;
        await ConsumeInteractionItemAsync(player, hand);
        ConversionTime = Random.Next(3600, 6001);
        ConversionStarter = player.Uuid;
        RemovePotionEffect((int)PotionEffect.Weakness - 1);
        AddPotionEffect((int)PotionEffect.Strength - 1, ConversionTime, Math.Max(0, (int)Level.LevelData.Difficulty - 1));
        SendEntityEvent(16);
        SynchronizeMetadata();
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (IsRemoved || ConversionTime < 0 || MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        var progress = 1;
        if (Random.NextSingle() < 0.01f)
        {
            var position = (Vector)Position.Floor();
            var specialBlocks = 0;
            for (var x = -4; x < 4 && specialBlocks < 14; x++)
            for (var y = -4; y < 4 && specialBlocks < 14; y++)
            for (var z = -4; z < 4 && specialBlocks < 14; z++)
            {
                var block = Terrain.GetBlock(position + new Vector(x, y, z));
                if (block == null || block.Material != Material.IronBars && !TagsRegistry.Block.Beds.Entries.Contains(block.RegistryId)) continue;
                specialBlocks++;
                if (Random.NextSingle() < 0.3f) progress++;
            }
        }
        ConversionTime -= progress;
        if (ConversionTime > 0) return;
        var villager = (Villager)await ConvertToAsync(EntityType.Villager);
        villager.VillagerType = VillagerType;
        villager.VillagerProfession = VillagerProfession;
        villager.VillagerLevel = VillagerLevel;
        villager.VillagerXp = VillagerXp;
        villager.IsBaby = IsBaby;
        villager.PersistenceRequired = true;
        villager.UnmodeledData = UnmodeledData;
        villager.UnmodeledData.Remove("ConversionTime");
        villager.UnmodeledData.Remove("ConversionPlayer");
        villager.AddPotionEffect((int)PotionEffect.Nausea - 1, 200);
        villager.Health = villager.GetAttributeValue("minecraft:generic.max_health");
        villager.SynchronizeMetadata();
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(19, EntityMetadataType.Boolean); writer.WriteBoolean(ConversionTime >= 0);
        writer.WriteEntityMetadataType(20, EntityMetadataType.VillagerData);
        writer.WriteVarInt(VillagerType); writer.WriteVarInt(VillagerProfession); writer.WriteVarInt(VillagerLevel);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteInt("ConversionTime", ConversionTime);
        if (ConversionStarter != Guid.Empty) writer.WriteArray("ConversionPlayer", EntityNbt.UuidToInts(ConversionStarter));
        writer.WriteInt("Xp", VillagerXp);
        writer.WriteCompoundStart("VillagerData");
        writer.WriteString("type", $"minecraft:{Villager.BiomeTypes[Math.Clamp(VillagerType, 0, Villager.BiomeTypes.Length - 1)]}");
        writer.WriteString("profession", $"minecraft:{Villager.Professions[Math.Clamp(VillagerProfession, 0, Villager.Professions.Length - 1)]}");
        writer.WriteInt("level", VillagerLevel);
        writer.EndCompound();
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        ConversionTime = tag.TryGetTagValue<int>("ConversionTime", out var ticks) ? ticks : -1;
        VillagerXp = tag.TryGetTagValue<int>("Xp", out var xp) ? xp : 0;
        if (tag.TryGetTag<NbtArray<int>>("ConversionPlayer", out var starter) && starter.Count == 4) ConversionStarter = EntityNbt.UuidFromInts(starter.GetArray());
        if (tag.TryGetTag<NbtCompound>("VillagerData", out var data))
        {
            if (data.TryGetTagValue<int>("level", out var level)) VillagerLevel = Math.Clamp(level, 1, 5);
            if (data.TryGetTagValue<string>("type", out var type)) VillagerType = Math.Max(0, Array.IndexOf(Villager.BiomeTypes, type.Replace("minecraft:", "")));
            if (data.TryGetTagValue<string>("profession", out var profession)) VillagerProfession = Math.Max(0, Array.IndexOf(Villager.Professions, profession.Replace("minecraft:", "")));
        }
    }
}
