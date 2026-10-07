using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Text.Json;

namespace Obsidian.Entities;

public partial class Player
{
    private static readonly Dictionary<string, JsonElement> foodDefaults = LoadFoodDefaults();

    private static Dictionary<string, JsonElement> LoadFoodDefaults()
    {
        using var stream = typeof(Player).Assembly.GetManifestResourceStream("Obsidian.Assets.item_components.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject()
            .Where(item => item.Value.GetProperty("components").TryGetProperty("minecraft:food", out _))
            .ToDictionary(item => item.Name, item => item.Value.GetProperty("components").Clone());
    }

    private static (int Nutrition, float Saturation, bool AlwaysEat, float Seconds)? GetFood(ItemStack? item)
    {
        if (item?.Type == Material.OminousBottle) return (0, 0, true, 1.6f);
        if (item == null || item.RemoveComponents.Contains(DataComponentType.Food) || item.RemoveComponents.Contains(DataComponentType.Consumable)) return null;
        foodDefaults.TryGetValue(item.Holder.UnlocalizedName, out var defaults);
        var seconds = item.GetComponent<ConsumableDataComponent>(DataComponentType.Consumable)?.ConsumeSeconds ??
            (defaults.ValueKind != JsonValueKind.Undefined && defaults.GetProperty("minecraft:consumable").TryGetProperty("consume_seconds", out var duration) ? duration.GetSingle() : 1.6f);
        if (item.GetComponent<FoodDataComponent>(DataComponentType.Food) is { } custom)
            return (custom.Nutrition, custom.SaturationModifier, custom.CanAlwaysEat, seconds);
        if (defaults.ValueKind == JsonValueKind.Undefined) return null;
        var food = defaults.GetProperty("minecraft:food");
        return (food.GetProperty("nutrition").GetInt32(), food.GetProperty("saturation").GetSingle(),
            food.TryGetProperty("can_always_eat", out var always) && always.GetBoolean(), seconds);
    }
    private ItemStack? eatingItem;
    private int eatingSlot;
    private int eatingTicks;
    private VectorD? foodPosition;
    private bool foodGrounded;
    private float sentHealth = -1;
    private int sentFood = -1;
    private float sentSaturation = -1;

    internal void AddExhaustion(float amount)
    {
        if (Alive && GameMode is not GameMode.Creative and not GameMode.Spectator && Level.LevelData.Difficulty != Difficulty.Peaceful)
            FoodExhaustionLevel = Math.Min(40, FoodExhaustionLevel + amount);
    }

    internal void StartEating(InteractionHand hand)
    {
        if (!Alive || Respawning || GameMode == GameMode.Spectator || eatingItem != null) return;
        var slot = hand == InteractionHand.OffHand ? 45 : CurrentHeldItemSlot;
        var item = Inventory.GetItem(slot);
        if (item is not { Count: > 0 } || GetFood(item) is not { } food || FoodLevel >= 20 && !food.AlwaysEat && GameMode != GameMode.Creative) return;
        eatingItem = item;
        eatingSlot = slot;
        var duration = food.Seconds;
        eatingTicks = float.IsFinite(duration) ? Math.Max(1, (int)Math.Ceiling(duration * 20)) : 32;
        LivingBitMask |= LivingBitMask.HandActive;
        if (hand == InteractionHand.OffHand) LivingBitMask |= LivingBitMask.ActiveHand;
        else LivingBitMask &= ~LivingBitMask.ActiveHand;
        SyncFoodMetadata();
    }

    internal void CancelEating()
    {
        if (eatingItem == null) return;
        eatingItem = null;
        eatingTicks = 0;
        LivingBitMask &= ~(LivingBitMask.HandActive | LivingBitMask.ActiveHand);
        SyncFoodMetadata();
    }

    private void SyncFoodMetadata() => PacketBroadcaster.QueuePacketToLevel(Level,
        new SetEntityDataPacket { EntityId = EntityId, Entity = this });

    private void ApplyFoodEffects(ItemStack item)
    {
        if (item.Type == Material.OminousBottle)
        {
            var amplifier = item.GetComponent<SimpleDataComponent<int>>(DataComponentType.OminousBottleAmplifier)?.Value ?? 0;
            AddPotionEffect((int)PotionEffect.BadOmen - 1, 120000, Math.Clamp(amplifier, 0, 4));
            return;
        }
        if (item.GetComponent<ConsumableDataComponent>(DataComponentType.Consumable) is { } custom)
        {
            foreach (var effect in custom.Effects)
                if (effect.Effect is Obsidian.API.Effects.EffectWithProbability applied && Globals.Random.NextSingle() < applied.Probability)
                    AddPotionEffect(applied.EffectData.Id, applied.EffectData.Duration, applied.EffectData.Amplifier);
            return;
        }
        if (!foodDefaults.TryGetValue(item.Holder.UnlocalizedName, out var defaults) ||
            !defaults.GetProperty("minecraft:consumable").TryGetProperty("on_consume_effects", out var effects)) return;
        foreach (var effect in effects.EnumerateArray())
        {
            var type = effect.GetProperty("type").GetString();
            if (type == "minecraft:remove_effects" && item.Type == Material.HoneyBottle)
                RemovePotionEffect((int)PotionEffect.Poison - 1);
            if (type != "minecraft:apply_effects" || effect.TryGetProperty("probability", out var probability) && Globals.Random.NextSingle() >= probability.GetSingle()) continue;
            foreach (var applied in effect.GetProperty("effects").EnumerateArray())
            {
                var name = applied.GetProperty("id").GetString()!.Replace("minecraft:", "").Replace("_", "");
                if (!Enum.TryParse<PotionEffect>(name, true, out var potion)) continue;
                var duration = applied.TryGetProperty("duration", out var time) ? time.GetInt32() : 0;
                var amplifier = applied.TryGetProperty("amplifier", out var level) ? level.GetInt32() : 0;
                AddPotionEffect((int)potion - 1, duration, amplifier);
                if (potion == PotionEffect.Absorption)
                {
                    Absorption = 4 * (amplifier + 1);
                    SyncFoodMetadata();
                }
            }
        }
    }

    public override async ValueTask TickAsync()
    {
        if (damageCooldown > 0) damageCooldown--;
        if (HurtTime > 0) HurtTime--;
        if (pendingRespawnChunks && !Respawning && --respawnChunkRetryTicks <= 0)
        {
            pendingRespawnChunks = !await UpdateChunksAsync();
            respawnChunkRetryTicks = 20;
        }
        await base.TickAsync();
        if (!Alive || Respawning)
        {
            foodPosition = null;
            CancelEating();
            return;
        }
        TimeSinceRest = Sleeping ? 0 : (int)Math.Min(int.MaxValue, (long)TimeSinceRest + 1);
        var grounded = MovementFlags.HasFlag(MovementFlags.OnGround);
        if (foodPosition is VectorD previous)
        {
            var delta = Position - previous;
            var distance = (float)Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
            // Teleports do not count as exercise.
            if (distance < 2)
            {
                if (Swimming) AddExhaustion(distance * 0.01f);
                else if (Sprinting) AddExhaustion(distance * 0.1f);
                if (foodGrounded && !grounded && delta.Y > 0) AddExhaustion(Sprinting ? 0.2f : 0.05f);
            }
        }
        foodPosition = Position;
        foodGrounded = grounded;
        if (eatingItem is { } item)
        {
            if (!ReferenceEquals(Inventory.GetItem(eatingSlot), item) || item.Count <= 0 || eatingSlot != 45 && eatingSlot != CurrentHeldItemSlot)
                CancelEating();
            else if (--eatingTicks <= 0)
            {
                if (GetFood(item) is { } food)
                {
                    FoodLevel = Math.Min(20, FoodLevel + food.Nutrition);
                    FoodSaturationLevel = Math.Min(FoodLevel, FoodSaturationLevel + food.Saturation);
                    ApplyFoodEffects(item);
                    if (GameMode != GameMode.Creative)
                    {
                        Inventory.RemoveItem(eatingSlot, 1);
                        if (item.Type is Material.MushroomStew or Material.RabbitStew or Material.BeetrootSoup or Material.SuspiciousStew)
                            Inventory.SetItem(eatingSlot, new ItemStack(ItemsRegistry.Bowl));
                        else if (item.Type is Material.HoneyBottle or Material.OminousBottle)
                        {
                            if (item.Count <= 0) Inventory.SetItem(eatingSlot, new ItemStack(ItemsRegistry.GlassBottle));
                            else
                            {
                                var remainderSlot = Inventory.AddItem(new ItemStack(ItemsRegistry.GlassBottle));
                                if (remainderSlot >= 0)
                                    await Client.QueuePacketAsync(new ContainerSetSlotPacket
                                    { ContainerId = 0, Slot = (short)remainderSlot, SlotData = Inventory.GetItem(remainderSlot), StateId = Inventory.StateId++ });
                                else
                                {
                                    var dropped = new ItemEntity
                                    { EntityId = Obsidian.Server.GetNextEntityId(), Level = Level, Position = Position, Item = new ItemStack(ItemsRegistry.GlassBottle) };
                                    if (Level.TryAddEntity(dropped)) dropped.SpawnEntity();
                                }
                            }
                        }
                        await Client.QueuePacketAsync(new ContainerSetSlotPacket
                        { ContainerId = 0, Slot = (short)eatingSlot, SlotData = Inventory.GetItem(eatingSlot), StateId = Inventory.StateId++ });
                    }
                    await Client.QueuePacketAsync(new EntityEventPacket { EntityId = EntityId, Event = 9 });
                }
                CancelEating();
            }
        }
        if (GameMode is not GameMode.Creative and not GameMode.Spectator)
        {
            if (ActivePotionEffects.TryGetValue((int)PotionEffect.Hunger - 1, out var hunger))
                AddExhaustion(0.005f * (hunger.EffectData.Amplifier + 1));
            if (FoodExhaustionLevel > 4)
            {
                FoodExhaustionLevel -= 4;
                if (FoodSaturationLevel > 0) FoodSaturationLevel = Math.Max(0, FoodSaturationLevel - 1);
                else if (Level.LevelData.Difficulty != Difficulty.Peaceful) FoodLevel = Math.Max(0, FoodLevel - 1);
            }
            if (Level.LevelData.Difficulty == Difficulty.Peaceful)
            {
                if (++FoodTickTimer >= 20)
                {
                    FoodTickTimer = 0;
                    FoodLevel = Math.Min(20, FoodLevel + 1);
                    Health = Math.Min(20, Health + 1);
                }
            }
            else if (Health < 20 && FoodLevel >= 18)
            {
                var saturated = FoodLevel == 20 && FoodSaturationLevel > 0;
                if (++FoodTickTimer >= (saturated ? 10 : 80))
                {
                    var healing = saturated ? Math.Min(FoodSaturationLevel, 6) / 6 : 1;
                    Health = Math.Min(20, Health + healing);
                    AddExhaustion(healing * 6);
                    FoodTickTimer = 0;
                }
            }
            else if (FoodLevel == 0)
            {
                if (++FoodTickTimer >= 80)
                {
                    FoodTickTimer = 0;
                    var minimumHealth = Level.LevelData.Difficulty switch { Difficulty.Easy => 10, Difficulty.Normal => 1, _ => 0 };
                    if (Health > minimumHealth) await DamageAsync(this, Math.Min(1, Health - minimumHealth));
                }
            }
            else FoodTickTimer = 0;
        }
        if (Health != sentHealth || FoodLevel != sentFood || FoodSaturationLevel != sentSaturation)
        {
            await Client.QueuePacketAsync(new SetHealthPacket(Math.Max(0, Health), FoodLevel, FoodSaturationLevel));
            sentHealth = Health;
            sentFood = FoodLevel;
            sentSaturation = FoodSaturationLevel;
        }
    }
}
