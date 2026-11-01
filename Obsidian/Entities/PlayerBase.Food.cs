using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Text.Json;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    private static readonly Dictionary<string, JsonElement> foodDefaults = LoadFoodDefaults();

    private static Dictionary<string, JsonElement> LoadFoodDefaults()
    {
        using var stream = typeof(PlayerBase).Assembly.GetManifestResourceStream("Obsidian.Assets.item_components.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject()
            .Where(item => item.Value.GetProperty("components").TryGetProperty("minecraft:food", out _))
            .ToDictionary(item => item.Name, item => item.Value.GetProperty("components").Clone());
    }

    internal static (int Nutrition, float Saturation, bool AlwaysEat, float Seconds)? GetFood(ItemStack? item)
    {
        if (item?.Is(ItemsRegistry.OminousBottle) == true) return (0, 0, true, 1.6f);
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

    void IPlayer.AddExhaustion(float amount) => this.AddExhaustion(amount);

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
        if (item.Is(ItemsRegistry.OminousBottle))
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
            if (type == "minecraft:remove_effects" && item.Is(ItemsRegistry.HoneyBottle))
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

    public override ValueTask TickAsync() => this.RunTickAsync(this.TickPlayerAsync);

    // Like vanilla's LivingEntity.baseTick: a player loses air with their eyes in water, unless they can breathe there
    // or can't be hurt, and drowns once it runs out; out of it they get it back. Their client shows it as bubbles.
    private async ValueTask TickAirSupplyAsync()
    {
        var previous = Air;
        var eyeHeight = Swimming ? 0.4 : Sneaking ? 1.27 : 1.62;
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
            Air = (short)Math.Min(300, Air + 4);

        if (Air != previous)
            await this.QueuePacketAsync(new SetEntityDataPacket { EntityId = EntityId, Entity = this });
    }

    /// <summary>
    /// Player freezing handling
    /// </summary>
    /// <param name="feet"></param>
    /// <returns></returns>
    private async ValueTask TickFreezingAsync(IBlock? feet)
    {
        var previousFrozenTicks = PowderedSnowTicks;

        var hasFreezeProtection = false;

        for (var slot = 5; slot <= 8; slot++)
        {
            var item = Inventory.GetItem(slot);

            if (item is { Count: > 0 }
                && TagsRegistry.Item.FreezeImmuneWearables.Entries.Contains(item.Holder.Id))
            {
                hasFreezeProtection = true;
                break;
            }
        }

        var canFreeze = GameMode is GameMode.Survival or GameMode.Adventure
            && feet?.RegistryId == BlockIds.PowderSnow
            && !TagsRegistry.EntityType.FreezeImmuneEntityTypes.Entries.Contains(TypeId)
            && !hasFreezeProtection;

        if (canFreeze)
            PowderedSnowTicks = Math.Min(140, PowderedSnowTicks + 1);
        else
            PowderedSnowTicks = Math.Max(0, PowderedSnowTicks - 2);

        if (previousFrozenTicks != PowderedSnowTicks)
        {
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, 
                new SetEntityDataPacket
                {
                    EntityId = EntityId,
                    Entity = this
                });
        }

        if (PowderedSnowTicks >= 140 && Level.LevelData.Time % 40 == 0)
            await DamageCombatAsync(this, 1, CombatDamageKind.Freezing);
    }

    private async ValueTask TickEnvironmentAsync()
    {
        if (GameMode is GameMode.Creative or GameMode.Spectator
            || Abilities.HasFlag(PlayerAbility.Invulnerable))
                return;

        var terrain = new MobTerrain(Level);
        var bounds = Dimension.CreateBBFromPosition(Position);

        // Contact with lava
        if (!IsFireImmune && terrain.ContainsFluid(bounds, false))
        {
            Ignite(15);
            await DamageCombatAsync(this, 4, CombatDamageKind.FireContact);

            if (!Alive || Respawning)
                return;
        }

        // Contact with cactus and wither rose
        for (var x = (int)Math.Floor(bounds.Min.X + 0.001); x <= (int)Math.Floor(bounds.Max.X - 0.001); x++)
            for (var y = (int)Math.Floor(bounds.Min.Y + 0.001); y <= (int)Math.Floor(bounds.Max.Y - 0.001); y++)
                for (var z = (int)Math.Floor(bounds.Min.Z + 0.001); z <= (int)Math.Floor(bounds.Max.Z - 0.001); z++)
                {
                    var block = terrain.GetBlock(new Vector(x, y, z));

                    if (block?.RegistryId == BlockIds.Cactus)
                    {
                        await DamageCombatAsync(this, 1, CombatDamageKind.Melee); // TODO CombatDamageKind currently don't have cactus damage type

                        if (!Alive || Respawning)
                            return;
                    }
                    if (Level.LevelData.Difficulty != Difficulty.Peaceful 
                        && block?.RegistryId == BlockIds.WitherRose)
                            AddPotionEffect((int)PotionEffect.Wither - 1, 40);
                }

        // Contact with magma block
        var floorBlock = terrain.GetBlock((Vector)(Position - new VectorD(0, 0.01f, 0)).Floor());
        if (floorBlock?.RegistryId == BlockIds.MagmaBlock
            && !IsFireImmune
            && !Sneaking
            && !HasPotionEffect((int)PotionEffect.FireResistance - 1))
        {
            await DamageCombatAsync(this, 1, CombatDamageKind.FireContact);

            if (!Alive || Respawning)
                return;
        }

        // Void handling
        var minimumY = Level is WorldData.AbstractLevel level ? level.MinY : -64;
        if (Position.Y < minimumY - 64)
        {
            await DamageCombatAsync(this, 4, CombatDamageKind.Void);
            if (!Alive || Respawning)
                return;
        }

        // Contact with fire and soulfire
        var feet = terrain.GetBlock((Vector)Position.Floor());
        if (feet?.RegistryId is BlockIds.Fire or BlockIds.SoulFire && !IsFireImmune)
        {
            Ignite(8);
            await DamageCombatAsync(this, 1, CombatDamageKind.FireContact);

            if (!Alive || Respawning)
                return;
        }

        // Contact with sweet berry
        if (feet?.RegistryId == BlockIds.SweetBerryBush &&
            (LastPosition - Position).MagnitudeSquared() > 0.000025f)
        {
            await DamageCombatAsync(this, 1, CombatDamageKind.Melee);

            if (!Alive || Respawning)
                return;
        }

        // Suffocation handling
        var width = Dimension.Width * 0.8 / 2;
        var eye = new BoundingBox(EyePosition(this) - new VectorD(width, 0.0000005, width),
            EyePosition(this) + new VectorD(width, 0.0000005, width));
        if (terrain.ContainsSuffocatingBlock(eye))
        {
            await DamageCombatAsync(this, 1, CombatDamageKind.Suffocation);

            if (!Alive || Respawning)
                return;
        }

        // Freezing handling
        await TickFreezingAsync(feet);
    }

    private async ValueTask TickPlayerAsync()
    {
        if (damageCooldown > 0) damageCooldown--;
        if (HurtTime > 0) HurtTime--;
        await TickCombatAsync();
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
        await TickEnvironmentAsync();
        if (!Alive)
            return;
        await TickInventoryItemsAsync();
        await TickAirSupplyAsync();
        if (!Alive)
            return;
        await TickSleepAsync();
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
                    this.Statistics.Increment(item.Holder.UnlocalizedName, type: "minecraft:used");
                    await this.FireProgressAsync("consume_item", item: new ItemStack(item, item.Count));
                    if (GameMode != GameMode.Creative)
                    {
                        Inventory.RemoveItem(eatingSlot, 1);
                        if (item.Holder.Id is ItemIds.MushroomStew
                            or ItemIds.RabbitStew
                            or ItemIds.BeetrootSoup
                            or ItemIds.SuspiciousStew)
                            Inventory.SetItem(eatingSlot, new ItemStack(ItemsRegistry.Bowl));
                        else if (item.Holder.Id is ItemIds.HoneyBottle or ItemIds.OminousBottle)
                        {
                            if (item.Count <= 0) Inventory.SetItem(eatingSlot, new ItemStack(ItemsRegistry.GlassBottle));
                            else
                            {
                                var remainderSlot = Inventory.AddItem(new ItemStack(ItemsRegistry.GlassBottle));
                                if (remainderSlot >= 0)
                                    await this.QueuePacketAsync(new ContainerSetSlotPacket
                                    { ContainerId = 0, Slot = (short)remainderSlot, SlotData = Inventory.GetItem(remainderSlot), StateId = Inventory.StateId++ });
                                else
                                {
                                    var dropped = new ItemEntity
                                    { EntityId = Obsidian.Server.GetNextEntityId(), Level = Level, Position = Position, Item = new ItemStack(ItemsRegistry.GlassBottle) };
                                    if (Level.TryAddEntity(dropped)) dropped.SpawnEntity();
                                }
                            }
                        }
                        await this.QueuePacketAsync(new ContainerSetSlotPacket
                        { ContainerId = 0, Slot = (short)eatingSlot, SlotData = Inventory.GetItem(eatingSlot), StateId = Inventory.StateId++ });
                    }
                    await this.QueuePacketAsync(new EntityEventPacket { EntityId = EntityId, Event = 9 });
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
                    Health = Math.Min(MaxHealth, Health + 1);
                }
            }
            else if (Health < MaxHealth && FoodLevel >= 18)
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
            else if (FoodLevel == 0)
            {
                if (++FoodTickTimer >= 80)
                {
                    FoodTickTimer = 0;
                    var minimumHealth = Level.LevelData.Difficulty switch { Difficulty.Easy => 10, Difficulty.Normal => 1, _ => 0 };
                    if (Health > minimumHealth) await DamageCombatAsync(this, Math.Min(1, Health - minimumHealth), CombatDamageKind.Starvation);
                }
            }
            else FoodTickTimer = 0;
        }
        if (Health != sentHealth || FoodLevel != sentFood || FoodSaturationLevel != sentSaturation)
        {
            await this.QueuePacketAsync(new SetHealthPacket(Math.Max(0, Health), FoodLevel, FoodSaturationLevel));
            sentHealth = Health;
            sentFood = FoodLevel;
            sentSaturation = FoodSaturationLevel;
        }
    }
}
