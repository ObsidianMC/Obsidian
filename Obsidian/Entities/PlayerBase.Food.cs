using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Text.Json;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    private static readonly Dictionary<string, JsonElement> foodDefaults = LoadFoodDefaults();

    private static Dictionary<string, JsonElement> LoadFoodDefaults()
    {
        var assembly = typeof(PlayerBase).Assembly;
        using var stream = assembly.GetManifestResourceStream("Obsidian.Assets.item_components.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject()
            .Where(item => item.Value.GetProperty("components").TryGetProperty("minecraft:food", out _))
            .ToDictionary(item => item.Name, item => item.Value.GetProperty("components").Clone());
    }

    internal static (int Nutrition, float Saturation, bool AlwaysEat, float Seconds)? GetFood(ItemStack? item)
    {
        if (item?.Is(ItemsRegistry.OminousBottle) == true)
            return (0, 0, true, 1.6f);

        if (item == null
            || item.RemoveComponents.Contains(DataComponentType.Food)
            || item.RemoveComponents.Contains(DataComponentType.Consumable))
        {
            return null;
        }

        foodDefaults.TryGetValue(item.Holder.UnlocalizedName, out var defaults);
        var consumable = item.GetComponent<ConsumableDataComponent>(DataComponentType.Consumable);
        var seconds = consumable?.ConsumeSeconds ?? GetDefaultConsumeSeconds(defaults);
        if (item.GetComponent<FoodDataComponent>(DataComponentType.Food) is { } custom)
            return (custom.Nutrition, custom.SaturationModifier, custom.CanAlwaysEat, seconds);

        if (defaults.ValueKind == JsonValueKind.Undefined)
            return null;

        var food = defaults.GetProperty("minecraft:food");
        var nutrition = food.GetProperty("nutrition").GetInt32();
        var saturation = food.GetProperty("saturation").GetSingle();
        var alwaysEat = food.TryGetProperty("can_always_eat", out var always) && always.GetBoolean();
        return (nutrition, saturation, alwaysEat, seconds);
    }

    private static float GetDefaultConsumeSeconds(JsonElement defaults)
    {
        if (defaults.ValueKind != JsonValueKind.Undefined
            && defaults.GetProperty("minecraft:consumable").TryGetProperty("consume_seconds", out var duration))
        {
            return duration.GetSingle();
        }

        return 1.6f;
    }

    private ItemStack? eatingItem;
    private int eatingSlot;
    private int eatingTicks;
    private VectorD? foodPosition;
    private bool foodGrounded;
    private float sentHealth = -1;
    private int sentFood = -1;
    private float sentSaturation = -1;

    void IPlayer.AddExhaustion(float amount) => this.AddExhaustion(amount);

    internal void AddExhaustion(float amount)
    {
        if (Alive
            && GameMode is not GameMode.Creative and not GameMode.Spectator
            && Level.LevelData.Difficulty != Difficulty.Peaceful)
        {
            FoodExhaustionLevel = Math.Min(40, FoodExhaustionLevel + amount);
        }
    }

    internal void StartEating(InteractionHand hand)
    {
        if (!Alive || Respawning || GameMode == GameMode.Spectator || eatingItem != null)
            return;

        var slot = hand == InteractionHand.OffHand ? 45 : CurrentHeldItemSlot;
        var item = Inventory.GetItem(slot);
        if (item is not { Count: > 0 } || GetFood(item) is not { } food)
            return;

        if (FoodLevel >= 20 && !food.AlwaysEat && GameMode != GameMode.Creative)
            return;

        eatingItem = item;
        eatingSlot = slot;
        var duration = food.Seconds;
        eatingTicks = float.IsFinite(duration) ? Math.Max(1, (int)Math.Ceiling(duration * 20)) : 32;
        LivingBitMask |= LivingBitMask.HandActive;
        if (hand == InteractionHand.OffHand)
            LivingBitMask |= LivingBitMask.ActiveHand;
        else
            LivingBitMask &= ~LivingBitMask.ActiveHand;

        SyncFoodMetadata();
    }

    internal void CancelEating()
    {
        if (eatingItem == null)
            return;

        eatingItem = null;
        eatingTicks = 0;
        LivingBitMask &= ~(LivingBitMask.HandActive | LivingBitMask.ActiveHand);
        SyncFoodMetadata();
    }

    private void SyncFoodMetadata()
    {
        var packet = new SetEntityDataPacket { EntityId = EntityId, Entity = this };
        PacketBroadcaster.QueuePacketToLevel(Level, packet);
    }

    private void ApplyFoodEffects(ItemStack item)
    {
        if (item.Is(ItemsRegistry.OminousBottle))
        {
            var amplifierComponent = item.GetComponent<SimpleDataComponent<int>>(
                DataComponentType.OminousBottleAmplifier);
            var amplifier = amplifierComponent?.Value ?? 0;
            AddPotionEffect((int)PotionEffect.BadOmen - 1, 120000, Math.Clamp(amplifier, 0, 4));
            return;
        }

        if (item.GetComponent<ConsumableDataComponent>(DataComponentType.Consumable) is { } custom)
        {
            ApplyConsumableEffects(custom);
            return;
        }

        ApplyDefaultFoodEffects(item);
    }

    private void ApplyConsumableEffects(ConsumableDataComponent custom)
    {
        foreach (var effect in custom.Effects)
        {
            if (effect.Effect is Obsidian.API.Effects.EffectWithProbability applied
                && Globals.Random.NextSingle() < applied.Probability)
            {
                AddPotionEffect(applied.EffectData.Id, applied.EffectData.Duration, applied.EffectData.Amplifier);
            }
        }
    }

    /// <summary>The on-consume effects of the item's vanilla consumable component.</summary>
    private void ApplyDefaultFoodEffects(ItemStack item)
    {
        if (!foodDefaults.TryGetValue(item.Holder.UnlocalizedName, out var defaults)
            || !defaults.GetProperty("minecraft:consumable").TryGetProperty("on_consume_effects", out var effects))
        {
            return;
        }

        foreach (var effect in effects.EnumerateArray())
        {
            var type = effect.GetProperty("type").GetString();
            if (type == "minecraft:remove_effects" && item.Is(ItemsRegistry.HoneyBottle))
                RemovePotionEffect((int)PotionEffect.Poison - 1);

            if (type != "minecraft:apply_effects")
                continue;

            var missedChance = effect.TryGetProperty("probability", out var probability)
                && Globals.Random.NextSingle() >= probability.GetSingle();
            if (missedChance)
                continue;

            foreach (var applied in effect.GetProperty("effects").EnumerateArray())
                ApplyDefaultFoodEffect(applied);
        }
    }

    private void ApplyDefaultFoodEffect(JsonElement applied)
    {
        var name = applied.GetProperty("id").GetString()!.Replace("minecraft:", "").Replace("_", "");
        if (!Enum.TryParse<PotionEffect>(name, true, out var potion))
            return;

        var duration = applied.TryGetProperty("duration", out var time) ? time.GetInt32() : 0;
        var amplifier = applied.TryGetProperty("amplifier", out var level) ? level.GetInt32() : 0;
        AddPotionEffect((int)potion - 1, duration, amplifier);
        if (potion == PotionEffect.Absorption)
        {
            Absorption = 4 * (amplifier + 1);
            SyncFoodMetadata();
        }
    }

    public override ValueTask TickAsync() => this.RunTickAsync(this.TickPlayerAsync);

    // Like vanilla's LivingEntity.baseTick: a player loses air with their eyes in water, unless they can breathe there
    // or can't be hurt, and drowns once it runs out; out of it they get it back. Their client shows it as bubbles.
    private async ValueTask TickAirSupplyAsync()
    {
        var previous = Air;
        var eyeHeight = (Swimming, Sneaking) switch
        {
            (true, _) => 0.4,
            (_, true) => 1.27,
            _ => 1.62,
        };

        var eye = Position + new VectorD(0, eyeHeight, 0);
        var terrain = new Obsidian.Entities.AI.MobTerrain(Level);
        // Vanilla's LivingEntity.baseTick: a bubble column at the eye refills air like open air, though it is water.
        if (GameMode is GameMode.Survival or GameMode.Adventure
            && terrain.IsInFluid(eye, water: true)
            && terrain.GetBlock((Vector)eye.Floor())?.RegistryId != BlockIds.BubbleColumn
            && !HasPotionEffect((int)PotionEffect.WaterBreathing - 1)
            && !HasPotionEffect((int)PotionEffect.ConduitPower - 1))
        {
            // Respiration keeps a breath in with a chance of its level in its level plus one.
            var respiration = CombatItems.EnchantmentLevel(Inventory.GetItem(5), EnchantmentsRegistry.Respiration);
            if (respiration <= 0 || Globals.Random.Next(respiration + 1) == 0)
                Air--;

            if (Air <= -20)
            {
                Air = 0;
                await DamageCombatAsync(this, 2, CombatDamageKind.Drowning);
            }
        }
        else if (Air < 300)
        {
            Air = (short)Math.Min(300, Air + 4);
        }

        if (Air != previous)
            await this.QueuePacketAsync(new SetEntityDataPacket { EntityId = EntityId, Entity = this });
    }

    private async ValueTask TickPlayerAsync()
    {
        Obsidian.Commands.CommandSpam.Tick(this);

        if (damageCooldown > 0)
            damageCooldown--;

        if (HurtTime > 0)
            HurtTime--;

        await TickCombatAsync();
        await TickCameraAsync();
        if (pendingRespawnChunks && !Respawning && --respawnChunkRetryTicks <= 0)
        {
            pendingRespawnChunks = !await UpdateChunksAsync();
            respawnChunkRetryTicks = 20;
        }

        await base.TickAsync();
        await this.TickProgressAsync();
        if (!Alive || Respawning)
        {
            foodPosition = null;
            CancelEating();
            CancelWeaponUse();
            return;
        }

        await TickInventoryItemsAsync();
        await TickAirSupplyAsync();
        if (!Alive)
            return;

        await TickSleepAsync();
        TimeSinceRest = Sleeping ? 0 : (int)Math.Min(int.MaxValue, (long)TimeSinceRest + 1);
        TickMovementExhaustion();
        if (eatingItem is { } item)
            await TickEatingAsync(item);

        if (GameMode is not GameMode.Creative and not GameMode.Spectator)
            await TickHungerAsync();

        await SyncHealthBarAsync();
    }

    /// <summary>Swimming, sprinting and jumping since the last tick tire the player.</summary>
    private void TickMovementExhaustion()
    {
        var grounded = MovementFlags.HasFlag(MovementFlags.OnGround);
        if (foodPosition is VectorD previous)
        {
            var delta = Position - previous;
            var distance = (float)Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
            // Teleports do not count as exercise.
            if (distance < 2)
            {
                if (Swimming)
                    AddExhaustion(distance * 0.01f);
                else if (Sprinting)
                    AddExhaustion(distance * 0.1f);

                if (foodGrounded && !grounded && delta.Y > 0)
                    AddExhaustion(Sprinting ? 0.2f : 0.05f);
            }
        }

        foodPosition = Position;
        foodGrounded = grounded;
    }

    private async ValueTask TickEatingAsync(ItemStack item)
    {
        var lostItem = !ReferenceEquals(Inventory.GetItem(eatingSlot), item)
            || item.Count <= 0
            || eatingSlot != 45 && eatingSlot != CurrentHeldItemSlot;
        if (lostItem)
        {
            CancelEating();
        }
        else if (--eatingTicks <= 0)
        {
            if (GetFood(item) is { } food)
                await FinishEatingAsync(item, food);

            CancelEating();
        }
    }

    private async ValueTask FinishEatingAsync(
        ItemStack item,
        (int Nutrition, float Saturation, bool AlwaysEat, float Seconds) food)
    {
        FoodLevel = Math.Min(20, FoodLevel + food.Nutrition);
        FoodSaturationLevel = Math.Min(FoodLevel, FoodSaturationLevel + food.Saturation);
        ApplyFoodEffects(item);
        this.Statistics.Increment(item.Holder.UnlocalizedName, type: "minecraft:used");
        await this.FireProgressAsync("consume_item", item: new ItemStack(item, item.Count));
        if (GameMode != GameMode.Creative)
        {
            Inventory.RemoveItem(eatingSlot, 1);
            if (item.Holder.Id is ItemIds.MushroomStew
                or ItemIds.RabbitStew
                or ItemIds.BeetrootSoup
                or ItemIds.SuspiciousStew)
            {
                Inventory.SetItem(eatingSlot, new ItemStack(ItemsRegistry.Bowl));
            }
            else if (item.Holder.Id is ItemIds.HoneyBottle or ItemIds.OminousBottle)
            {
                await ReturnGlassBottleAsync(item);
            }

            await QueueSlotUpdateAsync(eatingSlot);
        }

        await this.QueuePacketAsync(new EntityEventPacket { EntityId = EntityId, Event = 9 });
    }

    /// <summary>
    /// A drunk bottle leaves a glass bottle in its slot, or when the stack isn't used up, in the inventory or on the
    /// ground.
    /// </summary>
    private async ValueTask ReturnGlassBottleAsync(ItemStack item)
    {
        if (item.Count <= 0)
        {
            Inventory.SetItem(eatingSlot, new ItemStack(ItemsRegistry.GlassBottle));
            return;
        }

        var remainderSlot = Inventory.AddItem(new ItemStack(ItemsRegistry.GlassBottle));
        if (remainderSlot >= 0)
        {
            await QueueSlotUpdateAsync(remainderSlot);
            return;
        }

        var dropped = new ItemEntity
        {
            EntityId = Obsidian.Server.GetNextEntityId(),
            Level = Level,
            Position = Position,
            Item = new ItemStack(ItemsRegistry.GlassBottle),
        };

        if (Level.TryAddEntity(dropped))
            dropped.SpawnEntity();
    }

    private async ValueTask QueueSlotUpdateAsync(int slot)
    {
        var packet = new ContainerSetSlotPacket
        {
            ContainerId = 0,
            Slot = (short)slot,
            SlotData = Inventory.GetItem(slot),
            StateId = Inventory.StateId++,
        };

        await this.QueuePacketAsync(packet);
    }

    /// <summary>Hunger, exhaustion, natural regeneration and starvation, for players who can go hungry.</summary>
    private async ValueTask TickHungerAsync()
    {
        if (ActivePotionEffects.TryGetValue((int)PotionEffect.Hunger - 1, out var hunger))
            AddExhaustion(0.005f * (hunger.EffectData.Amplifier + 1));

        if (FoodExhaustionLevel > 4)
            SpendExhaustion();

        if (Level.LevelData.Difficulty == Difficulty.Peaceful)
            TickPeacefulRegeneration();
        else if (Health < MaxHealth && FoodLevel >= 18)
            TickFoodRegeneration();
        else if (FoodLevel == 0)
            await TickStarvationAsync();
        else
            FoodTickTimer = 0;
    }

    private void SpendExhaustion()
    {
        FoodExhaustionLevel -= 4;
        if (FoodSaturationLevel > 0)
            FoodSaturationLevel = Math.Max(0, FoodSaturationLevel - 1);
        else if (Level.LevelData.Difficulty != Difficulty.Peaceful)
            FoodLevel = Math.Max(0, FoodLevel - 1);
    }

    private void TickPeacefulRegeneration()
    {
        if (++FoodTickTimer >= 20)
        {
            FoodTickTimer = 0;
            FoodLevel = Math.Min(20, FoodLevel + 1);
            Health = Math.Min(MaxHealth, Health + 1);
        }
    }

    private void TickFoodRegeneration()
    {
        var saturated = FoodLevel == 20 && FoodSaturationLevel > 0;
        if (++FoodTickTimer >= (saturated ? 10 : 80))
        {
            var healing = saturated ? Math.Min(FoodSaturationLevel, 6) / 6 : 1;
            Health = Math.Min(MaxHealth, Health + healing);
            AddExhaustion(healing * 6);
            FoodTickTimer = 0;
        }
    }

    private async ValueTask TickStarvationAsync()
    {
        if (++FoodTickTimer >= 80)
        {
            FoodTickTimer = 0;
            var minimumHealth = Level.LevelData.Difficulty switch
            {
                Difficulty.Easy => 10,
                Difficulty.Normal => 1,
                _ => 0,
            };

            if (Health > minimumHealth)
                await DamageCombatAsync(this, Math.Min(1, Health - minimumHealth), CombatDamageKind.Starvation);
        }
    }

    private async ValueTask SyncHealthBarAsync()
    {
        if (Health != sentHealth || FoodLevel != sentFood || FoodSaturationLevel != sentSaturation)
        {
            await this.QueuePacketAsync(new SetHealthPacket(Math.Max(0, Health), FoodLevel, FoodSaturationLevel));
            sentHealth = Health;
            sentFood = FoodLevel;
            sentSaturation = FoodSaturationLevel;
        }
    }
}
