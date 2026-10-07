using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Net;

namespace Obsidian.WorldData.Generators;

internal sealed record MobTestExhibit(EntityType Type, string Label, bool Frozen, NbtCompound Data)
{
    internal bool Water => Type is EntityType.Axolotl or EntityType.Cod or EntityType.Dolphin or EntityType.Drowned or
        EntityType.ElderGuardian or EntityType.GlowSquid or EntityType.Guardian or EntityType.Nautilus or EntityType.Pufferfish or
        EntityType.Salmon or EntityType.Squid or EntityType.Tadpole or EntityType.TropicalFish or EntityType.ZombieNautilus;

    private static readonly EntityType[] mobs =
    [
        EntityType.Allay, EntityType.Armadillo, EntityType.Axolotl, EntityType.Bat, EntityType.Bee, EntityType.Blaze,
        EntityType.Bogged, EntityType.Breeze, EntityType.Camel, EntityType.CamelHusk, EntityType.Cat, EntityType.CaveSpider,
        EntityType.Chicken, EntityType.Cod, EntityType.CopperGolem, EntityType.Cow, EntityType.Creaking, EntityType.Creeper,
        EntityType.Dolphin, EntityType.Donkey, EntityType.Drowned, EntityType.ElderGuardian, EntityType.EnderDragon,
        EntityType.Enderman, EntityType.Endermite, EntityType.Evoker, EntityType.Fox, EntityType.Frog, EntityType.Ghast,
        EntityType.Giant, EntityType.GlowSquid, EntityType.Goat, EntityType.Guardian, EntityType.HappyGhast, EntityType.Hoglin,
        EntityType.Horse, EntityType.Husk, EntityType.Illusioner, EntityType.IronGolem, EntityType.Llama, EntityType.MagmaCube,
        EntityType.Mooshroom, EntityType.Mule, EntityType.Nautilus, EntityType.Ocelot, EntityType.Panda, EntityType.Parched,
        EntityType.Parrot, EntityType.Phantom, EntityType.Pig, EntityType.Piglin, EntityType.PiglinBrute, EntityType.Pillager,
        EntityType.PolarBear, EntityType.Pufferfish, EntityType.Rabbit, EntityType.Ravager, EntityType.Salmon, EntityType.Sheep,
        EntityType.Shulker, EntityType.Silverfish, EntityType.Skeleton, EntityType.SkeletonHorse, EntityType.Slime,
        EntityType.Sniffer, EntityType.SnowGolem, EntityType.Spider, EntityType.Squid, EntityType.Stray, EntityType.Strider,
        EntityType.Tadpole, EntityType.TraderLlama, EntityType.TropicalFish, EntityType.Turtle, EntityType.Vex,
        EntityType.Villager, EntityType.Vindicator, EntityType.WanderingTrader, EntityType.Warden, EntityType.Witch,
        EntityType.Wither, EntityType.WitherSkeleton, EntityType.Wolf, EntityType.Zoglin, EntityType.Zombie,
        EntityType.ZombieHorse, EntityType.ZombieNautilus, EntityType.ZombieVillager, EntityType.ZombifiedPiglin
    ];

    internal static IEnumerable<MobTestExhibit> Create(ILevel level)
    {
        foreach (var type in mobs)
        {
            var mob = EntitySpawner.CreateMob(level, type);
            yield return new(type, mob is { HasAi: true } ? "active" : "implementation unavailable", false, new());
            yield return new(type, "display", true, new());
            if (mob is AgeableMob || mob is Zombie or Piglin or Zoglin)
                yield return new(type, "baby", true, new() { new NbtTag<int>("Age", -24000), new NbtTag<bool>("IsBaby", true) });

            if (type is EntityType.Horse or EntityType.Donkey or EntityType.Mule or EntityType.Camel or
                EntityType.SkeletonHorse or EntityType.ZombieHorse or EntityType.Pig or EntityType.Strider or EntityType.Nautilus)
                yield return Equipped(type, "saddled", EquipmentSlot.Saddle, Material.Saddle);
            if (type is EntityType.Zombie or EntityType.Husk or EntityType.Drowned or EntityType.Skeleton or EntityType.Stray or
                EntityType.Bogged or EntityType.Parched or EntityType.WitherSkeleton or EntityType.Piglin or EntityType.ZombifiedPiglin)
                yield return Equipped(type, "armored", EquipmentSlot.Helmet, Material.DiamondHelmet, EquipmentSlot.Chestplate, Material.DiamondChestplate);

            switch (type)
            {
                case EntityType.Wither:
                    yield return new(type, "normal", true, new() { new NbtTag<int>("Invul", 0), new NbtTag<float>("Health", 300) });
                    yield return new(type, "armored", true, new() { new NbtTag<int>("Invul", 0), new NbtTag<float>("Health", 150) });
                    break;
                case EntityType.EnderDragon:
                    yield return State(type, "perched", "DragonPhase", 6);
                    break;
                case EntityType.Warden:
                    yield return State(type, "emerging", "ObsidianWardenPose", (int)Pose.Emerging);
                    yield return State(type, "digging", "ObsidianWardenPose", (int)Pose.Digging);
                    yield return State(type, "roaring", "ObsidianWardenPose", (int)Pose.Roaring);
                    break;
                case EntityType.Cat:
                    foreach (var variant in Cat.VariantNames) yield return State(type, variant, "variant", $"minecraft:{variant}");
                    break;
                case EntityType.Wolf:
                    foreach (var variant in Wolf.VariantNames) yield return State(type, variant, "variant", $"minecraft:{variant}");
                    foreach (var sound in Wolf.SoundVariantNames) yield return State(type, $"sound {sound}", "sound_variant", $"minecraft:{sound}");
                    break;
                case EntityType.Fox:
                    yield return State(type, "snow", "Type", "snow");
                    yield return State(type, "sleeping", "Sleeping", true);
                    break;
                case EntityType.Llama:
                    for (var variant = 0; variant < 4; variant++) yield return State(type, $"variant {variant}", "Variant", variant);
                    yield return State(type, "chested", "ChestedHorse", true);
                    break;
                case EntityType.Donkey:
                case EntityType.Mule:
                    yield return State(type, "chested", "ChestedHorse", true);
                    break;
                case EntityType.Sheep:
                case EntityType.Shulker:
                    for (byte color = 0; color < 16; color++) yield return State(type, $"color {color}", "Color", color);
                    if (type == EntityType.Sheep) yield return State(type, "sheared", "Sheared", true);
                    else yield return State(type, "open", "Peek", (byte)100);
                    break;
                case EntityType.Cow:
                case EntityType.Pig:
                case EntityType.Chicken:
                    for (var variant = 0; variant < 3; variant++) yield return State(type, $"variant {variant}", "ObsidianVariant", variant);
                    break;
                case EntityType.Axolotl:
                case EntityType.Parrot:
                    for (var variant = 0; variant < 5; variant++) yield return State(type, $"variant {variant}", "Variant", variant);
                    break;
                case EntityType.Frog:
                    foreach (var variant in new[] { "cold", "temperate", "warm" }) yield return State(type, variant, "variant", $"minecraft:{variant}");
                    break;
                case EntityType.Mooshroom:
                    yield return State(type, "brown", "Type", "brown");
                    break;
                case EntityType.Rabbit:
                    foreach (var variant in new[] { 0, 1, 2, 3, 4, 5, 99 }) yield return State(type, $"variant {variant}", "RabbitType", variant);
                    break;
                case EntityType.Salmon:
                    foreach (var size in new[] { "small", "medium", "large" }) yield return State(type, size, "type", size);
                    break;
                case EntityType.Pufferfish:
                    for (var puff = 1; puff <= 2; puff++) yield return State(type, $"puff {puff}", "PuffState", puff);
                    break;
                case EntityType.Slime:
                case EntityType.MagmaCube:
                    foreach (var size in new[] { 1, 2, 4 }) yield return State(type, $"size {size}", "Size", size - 1);
                    break;
                case EntityType.Phantom:
                    yield return State(type, "large", "size", 4);
                    break;
                case EntityType.Pillager:
                    yield return Equipped(type, "crossbow", EquipmentSlot.MainHand, Material.Crossbow);
                    break;
                case EntityType.HappyGhast:
                    yield return Equipped(type, "harness", EquipmentSlot.Body, Material.RedHarness);
                    break;
                case EntityType.Nautilus:
                    yield return Equipped(type, "body armor", EquipmentSlot.Body, Material.DiamondNautilusArmor);
                    break;
                case EntityType.Drowned:
                    yield return Equipped(type, "trident", EquipmentSlot.MainHand, Material.Trident);
                    break;
                case EntityType.Piglin:
                    yield return Equipped(type, "crossbow", EquipmentSlot.MainHand, Material.Crossbow);
                    yield return Equipped(type, "sword", EquipmentSlot.MainHand, Material.GoldenSword);
                    break;
                case EntityType.Creeper:
                    yield return State(type, "charged", "powered", true);
                    break;
                case EntityType.SnowGolem:
                    yield return State(type, "without pumpkin", "Pumpkin", false);
                    break;
                case EntityType.Bogged:
                    yield return State(type, "sheared", "sheared", true);
                    break;
                case EntityType.Goat:
                    yield return State(type, "screaming", "IsScreamingGoat", true);
                    yield return new(type, "without horns", true, new() { new NbtTag<bool>("HasLeftHorn", false), new NbtTag<bool>("HasRightHorn", false) });
                    break;
                case EntityType.Panda:
                    foreach (var gene in new[] { "normal", "lazy", "worried", "playful", "brown", "weak", "aggressive" })
                        yield return new(type, gene, true, new() { new NbtTag<string>("MainGene", gene), new NbtTag<string>("HiddenGene", gene) });
                    break;
                case EntityType.CopperGolem:
                    foreach (var weather in new[] { "unaffected", "exposed", "weathered", "oxidized" }) yield return State(type, weather, "weather_state", weather);
                    break;
                case EntityType.Armadillo:
                    foreach (var state in new[] { "rolling", "scared", "unrolling" }) yield return State(type, state, "state", state);
                    break;
                case EntityType.Turtle:
                    yield return State(type, "carrying eggs", "has_egg", true);
                    break;
                case EntityType.Horse:
                    for (var coat = 0; coat < 7; coat++)
                    for (var markings = 0; markings < 5; markings++) yield return State(type, $"coat {coat}, markings {markings}", "Variant", coat | markings << 8);
                    break;
                case EntityType.TropicalFish:
                    for (var shape = 0; shape < 2; shape++)
                    for (var pattern = 0; pattern < 6; pattern++) yield return State(type, $"shape {shape}, pattern {pattern}", "Variant", shape | pattern << 8 | pattern << 16 | (15 - pattern) << 24);
                    break;
            }
        }
    }

    private static MobTestExhibit State<T>(EntityType type, string label, string key, T value) =>
        new(type, label, true, new() { new NbtTag<T>(key, value) });

    private static MobTestExhibit Equipped(EntityType type, string label, EquipmentSlot slot, Material item,
        EquipmentSlot? secondSlot = null, Material secondItem = default)
    {
        var equipment = new NbtList(NbtTagType.Compound, "ObsidianEquipment") { Equipment(slot, item) };
        if (secondSlot is { } additional) equipment.Add(Equipment(additional, secondItem));
        return new(type, label, true, new() { equipment, new NbtTag<bool>("Tame", true) });
    }

    private static NbtCompound Equipment(EquipmentSlot slot, Material material)
    {
        var buffer = new NetworkBuffer();
        buffer.WriteItemStack(ItemsRegistry.GetSingleItem(material));
        return new()
        {
            new NbtTag<int>("slot", (int)slot), new NbtTag<float>("drop_chance", 0),
            new NbtArray<byte>("data", buffer.AsSpan(0, buffer.Size).ToArray())
        };
    }
}
