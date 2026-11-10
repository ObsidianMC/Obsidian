using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.WorldData.Fluids;
using System.Collections.Frozen;

namespace Obsidian.WorldData;

// The use behaviour of cauldrons.
internal static partial class UseBehaviors
{
    internal static IBlockUseBehavior AbstractCauldronBlock { get; } = new CauldronUse();

    /// <summary>
    /// Vanilla's <c>AbstractCauldronBlock.useItemOn</c> and the interactions of <c>CauldronInteractions</c>: buckets
    /// and bottles fill and empty a cauldron, and a water cauldron washes the dye out of leather and wolf armor, a
    /// banner's last pattern and a shulker box's colour, using up a level of water each time. What the hand holds and
    /// the kind of cauldron pick the interaction; anything else passes to the use without an item.
    /// </summary>
    /// <remarks>
    /// An exchange changes the hand and the cauldron in one <see cref="BlockUse.TryCommit"/>, so a hand that changes
    /// in the meantime, or another player using the same cauldron first, refuses the use rather than giving an item
    /// for nothing. The cauldron's sounds come from the block for everyone, the user included, and its game events
    /// have no source entity, as vanilla's do.
    /// </remarks>
    private sealed class CauldronUse : IBlockUseBehavior
    {
        private delegate ValueTask<UseResult> Interaction(BlockUse use, Cauldron cauldron);

        // The 16 dye colours, which name vanilla's banners and dyed shulker boxes (Items.BANNER and
        // Items.DYED_SHULKER_BOX).
        private static readonly string[] colors =
        [
            "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
            "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"
        ];

        // The interactions by cauldron and held item, as vanilla's dispatchers have them.
        private static readonly FrozenDictionary<(Cauldron, int), Interaction> interactions = BuildTable();

        public ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            if (!use.HasItem || KindOf(use.Block) is not Cauldron cauldron)
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);

            var item = use.Item!;

            // Vanilla's dispatcher checks its item tags before its items; only water cauldrons have one.
            if (cauldron == Cauldron.Water && TagsRegistry.Item.CauldronCanRemoveDye.Entries.Contains(item.Holder.Id))
                return WashDyeAsync(use, cauldron);

            if (!interactions.TryGetValue((cauldron, item.Holder.Id), out var interaction))
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);

            return interaction(use, cauldron);
        }

        private static FrozenDictionary<(Cauldron, int), Interaction> BuildTable()
        {
            var table = new Dictionary<(Cauldron, int), Interaction>();
            foreach (var cauldron in Enum.GetValues<Cauldron>())
            {
                table[(cauldron, ItemIds.WaterBucket)] = PourWaterAsync;
                table[(cauldron, ItemIds.LavaBucket)] = PourLavaAsync;
                table[(cauldron, ItemIds.PowderSnowBucket)] = PourPowderSnowAsync;
            }

            table[(Cauldron.Empty, ItemIds.Potion)] = PourBottleAsync;
            table[(Cauldron.Water, ItemIds.Potion)] = PourBottleAsync;
            table[(Cauldron.Water, ItemIds.GlassBottle)] = FillBottleAsync;
            table[(Cauldron.Water, ItemIds.Bucket)] = FillBucketAsync;
            table[(Cauldron.Lava, ItemIds.Bucket)] = FillBucketAsync;
            table[(Cauldron.PowderSnow, ItemIds.Bucket)] = FillBucketAsync;

            foreach (var color in colors)
            {
                table[(Cauldron.Water, ItemsRegistry.Get($"minecraft:{color}_banner").Id)] = WashBannerAsync;
                table[(Cauldron.Water, ItemsRegistry.Get($"minecraft:{color}_shulker_box").Id)] = WashShulkerBoxAsync;
            }

            return table.ToFrozenDictionary();
        }

        // Vanilla's fillWaterInteraction: a full water cauldron.
        private static ValueTask<UseResult> PourWaterAsync(BlockUse use, Cauldron cauldron)
        {
            var water = BlocksRegistry.GetDefault(BlockIds.WaterCauldron).WithProperty("level", MaxLevel);
            return EmptyBucketAsync(use, water, "minecraft:item.bucket.empty");
        }

        // Vanilla's fillLavaInteraction: a lava cauldron, unless water is above it.
        private static ValueTask<UseResult> PourLavaAsync(BlockUse use, Cauldron cauldron)
        {
            if (IsUnderWater(use))
                return ValueTask.FromResult(UseResult.Consume);

            var lava = BlocksRegistry.GetDefault(BlockIds.LavaCauldron);
            return EmptyBucketAsync(use, lava, "minecraft:item.bucket.empty_lava");
        }

        // Vanilla's fillPowderSnowInteraction: a full powder snow cauldron, unless water is above it.
        private static ValueTask<UseResult> PourPowderSnowAsync(BlockUse use, Cauldron cauldron)
        {
            if (IsUnderWater(use))
                return ValueTask.FromResult(UseResult.Consume);

            var snow = BlocksRegistry.GetDefault(BlockIds.PowderSnowCauldron).WithProperty("level", MaxLevel);
            return EmptyBucketAsync(use, snow, "minecraft:item.bucket.empty_powder_snow");
        }

        // Vanilla's emptyBucket: the cauldron takes the bucket's contents and the hand gets an empty bucket.
        private static ValueTask<UseResult> EmptyBucketAsync(BlockUse use, IBlock filled, string sound) =>
            ExchangeAsync(use, new Outcome
            {
                State = filled,
                Hand = () => BlockUseReturn.Exchange(use, new ItemStack(ItemsRegistry.Bucket, 1)),
                Statistic = "minecraft:fill_cauldron",
                AwardsItemUse = true,
                Sound = sound,
                FluidEvent = MobGameEvent.FluidPlace
            });

        // Vanilla's fillBucket: a full cauldron (any lava cauldron) empties into the bucket.
        private static ValueTask<UseResult> FillBucketAsync(BlockUse use, Cauldron cauldron)
        {
            if (cauldron != Cauldron.Lava && Level(use.Block) != MaxLevel)
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);

            var (filled, sound) = cauldron switch
            {
                Cauldron.Lava => (ItemsRegistry.LavaBucket, "minecraft:item.bucket.fill_lava"),
                Cauldron.PowderSnow => (ItemsRegistry.PowderSnowBucket, "minecraft:item.bucket.fill_powder_snow"),
                _ => (ItemsRegistry.WaterBucket, "minecraft:item.bucket.fill")
            };

            return ExchangeAsync(use, new Outcome
            {
                State = BlocksRegistry.Cauldron,
                Hand = () => BlockUseReturn.Exchange(use, new ItemStack(filled, 1)),
                Statistic = "minecraft:use_cauldron",
                AwardsItemUse = true,
                Sound = sound,
                FluidEvent = MobGameEvent.FluidPickup
            });
        }

        // Vanilla's water potion interactions: a water bottle fills an empty cauldron to its first level, or a water
        // cauldron by one level until it's full.
        private static ValueTask<UseResult> PourBottleAsync(BlockUse use, Cauldron cauldron)
        {
            if (cauldron == Cauldron.Water && Level(use.Block) == MaxLevel)
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);

            // Vanilla's PotionContents.is(WATER): plain water, without custom effects.
            var potion = use.Item!.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
            if (potion?.Potion != Potion.Water || !potion.CustomEffects.IsDefaultOrEmpty)
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);

            var filled = cauldron == Cauldron.Empty
                ? BlocksRegistry.GetDefault(BlockIds.WaterCauldron)
                : use.Block.WithProperty("level", Level(use.Block) + 1);

            return ExchangeAsync(use, new Outcome
            {
                State = filled,
                Hand = () => BlockUseReturn.Exchange(use, new ItemStack(ItemsRegistry.GlassBottle, 1)),
                Statistic = "minecraft:use_cauldron",
                AwardsItemUse = true,
                ReadsUsedItemAfterwards = cauldron == Cauldron.Water,
                Sound = "minecraft:item.bottle.empty",
                FluidEvent = MobGameEvent.FluidPlace
            });
        }

        // Vanilla's glass bottle interaction: a level of water becomes a water bottle.
        private static ValueTask<UseResult> FillBottleAsync(BlockUse use, Cauldron cauldron)
        {
            var waterBottle = new ItemStack(ItemsRegistry.Potion, 1,
                new PotionContentsDataComponent { Potion = Potion.Water });

            return ExchangeAsync(use, new Outcome
            {
                State = Lowered(use.Block),
                Hand = () => BlockUseReturn.Exchange(use, waterBottle),
                Statistic = "minecraft:use_cauldron",
                AwardsItemUse = true,
                Lowers = true,
                Sound = "minecraft:item.bottle.fill",
                FluidEvent = MobGameEvent.FluidPickup
            });
        }

        // Vanilla's dyedItemIteration: dyed armor loses its colour in the hand, the whole stack at once and in creative
        // mode too, and the cauldron a level.
        private static ValueTask<UseResult> WashDyeAsync(BlockUse use, Cauldron cauldron)
        {
            if (!use.Item!.ContainsKey(DataComponentType.DyedColor))
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);

            return ExchangeAsync(use, new Outcome
            {
                State = Lowered(use.Block),
                Hand = () => Undye(use),
                Statistic = "minecraft:clean_armor",
                Lowers = true
            });
        }

        // Vanilla's bannerInteraction: one banner of the stack loses its last pattern.
        private static ValueTask<UseResult> WashBannerAsync(BlockUse use, Cauldron cauldron)
        {
            var patterns = use.Item!.GetComponent<BannerPatternsDataComponent>(DataComponentType.BannerPatterns);
            if (patterns is null || patterns.Value.IsDefaultOrEmpty)
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);

            var cleaned = new ItemStack(use.Item, 1);
            var layers = patterns.Value.Length - 1;
            if (layers == 0)
            {
                // No patterns is the banner's default, which vanilla's set leaves out of the stack's components.
                RemoveComponent(cleaned, DataComponentType.BannerPatterns);
            }
            else
            {
                cleaned[DataComponentType.BannerPatterns] = patterns with
                {
                    Value = patterns.Value[..layers],
                    RegistryIds = [.. patterns.RegistryIds.Take(layers)]
                };
            }

            return ExchangeAsync(use, new Outcome
            {
                State = Lowered(use.Block),
                Hand = () => BlockUseReturn.Exchange(use, cleaned, limitCreative: false),
                Statistic = "minecraft:clean_banner",
                Lowers = true
            });
        }

        // Vanilla's shulkerBoxInteraction: one dyed shulker box of the stack becomes a plain one with the same
        // components, its contents included.
        private static ValueTask<UseResult> WashShulkerBoxAsync(BlockUse use, Cauldron cauldron)
        {
            var cleaned = new ItemStack(use.Item!.TransmuteCopy(ItemsRegistry.ShulkerBox), 1);

            return ExchangeAsync(use, new Outcome
            {
                State = Lowered(use.Block),
                Hand = () => BlockUseReturn.Exchange(use, cleaned, limitCreative: false),
                Statistic = "minecraft:clean_shulker_box",
                Lowers = true
            });
        }

        /// <summary>
        /// One interaction's effect: the cauldron's new state, what the hand becomes, and what follows.
        /// </summary>
        private sealed record Outcome
        {
            /// <summary>The cauldron's state afterwards, which may be the state it has.</summary>
            public required IBlock State { get; init; }

            /// <summary>Changes the hand inside the commit, through <see cref="BlockUseReturn"/>.</summary>
            public required Action Hand { get; init; }

            /// <summary>The <c>minecraft:custom</c> statistic the use awards.</summary>
            public required string Statistic { get; init; }

            /// <summary>Whether the item's <c>minecraft:used</c> statistic is awarded too.</summary>
            public bool AwardsItemUse { get; init; }

            /// <summary>
            /// Whether vanilla names that statistic's item from the hand after the exchange: a water bottle poured
            /// into a water cauldron does, so the last bottle of a stack awards <c>minecraft:air</c>.
            /// </summary>
            public bool ReadsUsedItemAfterwards { get; init; }

            /// <summary>
            /// Whether the cauldron lost a level (vanilla's <c>lowerFillLevel</c>), which emits a block change with the
            /// new state.
            /// </summary>
            public bool Lowers { get; init; }

            public string? Sound { get; init; }

            public MobGameEvent? FluidEvent { get; init; }
        }

        // The hand and the cauldron change together; then the statistics, the sound and the game events follow, in
        // vanilla's order.
        private static async ValueTask<UseResult> ExchangeAsync(BlockUse use, Outcome outcome)
        {
            var level = use.Level;
            var committed = use.TryCommit(() =>
            {
                outcome.Hand();

                // Setting the state the cauldron has (lava poured into lava) writes nothing, but the use still counts.
                level.BlockUpdates.SetBlock(use.Position, outcome.State, BlockUpdateFlags.All);
                return true;
            });
            if (!committed)
                return UseResult.Fail;

            await BlockUseReturn.SendChangedSlotsAsync(use);
            use.Award(outcome.Statistic);
            if (outcome.AwardsItemUse)
                use.Player.Statistics.Increment(UsedItemName(use, outcome), type: "minecraft:used");

            if (outcome.Lowers)
                level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, null, affectedBlock: outcome.State);

            if (outcome.Sound is string sound)
                BlockLighting.Play(level, use.Position, sound);

            if (outcome.FluidEvent is MobGameEvent fluidEvent)
                level.EmitGameEvent(fluidEvent, use.Center, null);

            return UseResult.Success;
        }

        // The item the minecraft:used statistic names. The hand after the exchange is empty when its last item was
        // exchanged outside creative mode, and an empty stack's item is air.
        private static string UsedItemName(BlockUse use, Outcome outcome)
        {
            var used = use.UsedItem!;
            var emptied = used.Count == 1 && use.Player.GameMode != GameMode.Creative;
            return outcome.ReadsUsedItemAfterwards && emptied
                ? ItemsRegistry.Air.UnlocalizedName
                : used.Holder.UnlocalizedName;
        }

        // Takes the dye out of the held stack, replacing it so that a use that read it before can't commit.
        private static void Undye(BlockUse use)
        {
            var inventory = use.Player.Inventory;
            var held = inventory.GetItem(use.HandSlot)!;
            var washed = new ItemStack(held, held.Count);
            RemoveComponent(washed, DataComponentType.DyedColor);

            inventory.SetItem(use.HandSlot, washed);
            use.ChangedInventorySlots.Add(use.HandSlot);
            use.ResyncHand = true;
        }

        // Removes a component from a stack's patch, leaving no removal behind when its item has no default of the type,
        // as vanilla's patched components do.
        private static void RemoveComponent(ItemStack stack, DataComponentType type)
        {
            stack.Remove(type);
            if (!stack.Holder.DefaultComponents.Any(component => component.Type == type))
                stack.RemoveComponents.Remove(type);
        }

        // Vanilla's isUnderWater: any water in the cell above, flowing or held by a waterlogged block.
        private static bool IsUnderWater(BlockUse use)
        {
            lock (use.Level.Fluids.TickLock)
                return FluidState.Of(use.Level.BlockUpdates.GetBlock(use.Position + Vector.Up)).IsWater;
        }

        /// <summary>The four kinds of cauldron, each with its own interactions.</summary>
        private enum Cauldron
        {
            Empty,
            Water,
            Lava,
            PowderSnow
        }

        // Vanilla's LayeredCauldronBlock.MAX_FILL_LEVEL.
        private const int MaxLevel = 3;

        private static Cauldron? KindOf(IBlock block) => block.RegistryId switch
        {
            BlockIds.Cauldron => Cauldron.Empty,
            BlockIds.WaterCauldron => Cauldron.Water,
            BlockIds.LavaCauldron => Cauldron.Lava,
            BlockIds.PowderSnowCauldron => Cauldron.PowderSnow,
            _ => null
        };

        private static int Level(IBlock cauldron) => int.Parse(cauldron.GetProperty("level")!);

        // Vanilla's LayeredCauldronBlock.lowerFillLevel: a level less, or an empty cauldron after the last.
        private static IBlock Lowered(IBlock cauldron)
        {
            var level = Level(cauldron) - 1;
            return level == 0 ? BlocksRegistry.Cauldron : cauldron.WithProperty("level", level);
        }
    }
}
