using Obsidian.API.Advancements;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.Progress;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Progress;
using System.IO;
using System.Text.Json;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    internal PlayerStatistics ProgressStatistics { get; } = new();
    internal PlayerAdvancements ProgressAdvancements => System.Threading.LazyInitializer.EnsureInitialized(
        ref this.progressAdvancements, () => new PlayerAdvancements(this));
    private PlayerAdvancements? progressAdvancements;

    public IStatistics Statistics => this.ProgressStatistics;
    public IAdvancements Advancements => this.ProgressAdvancements;
    private (ItemStack? Stack, long Version)[]? progressInventory;
    private string? progressDimension;
    private bool progressSleeping;
    private bool progressDead;
    private readonly HashSet<string> unlockedRecipes = [];
    private readonly ConcurrentQueue<string> craftedRecipes = new();

    /// <summary>Applies criteria and all their rewards as one player state change, unless the player left.</summary>
    internal async ValueTask<bool> GrantAdvancementAsync(Func<ValueTask<bool>> grant)
    {
        var changed = false;
        await this.RunTickAsync(async () =>
        {
            changed = await grant();
        });

        return changed;
    }

    void IPlayer.RecordProgressCraft(string identifier, ItemStack result) =>
        this.RecordProgressCraft(identifier, result);

    ValueTask IPlayer.RecordProgressKillAsync(IEntity victim) => this.RecordProgressKillAsync(victim);

    internal void RecordProgressCraft(string identifier, ItemStack result)
    {
        this.Statistics.Increment(result.Holder.UnlocalizedName, result.Count, "minecraft:crafted");
        this.craftedRecipes.Enqueue(AdvancementsRegistry.Qualify(identifier));
    }

    private string ProgressPath(string folder, Guid uuid)
    {
        var world = this.Level is IDimension dimension ? dimension.ParentWorld : (IWorld)this.Level;
        // Beside players/data: players/stats and players/advancements.
        return Path.Combine(Path.GetDirectoryName(world.PlayerDataPath)!, folder, $"{uuid}.json");
    }

    private void LoadProgress()
    {
        var statistics = ProgressFiles.Read(this.ProgressPath("stats", this.loadedUuid), this.Logger);
        this.ProgressStatistics.Load(statistics);
        if (statistics["stats"]?["minecraft:custom"]?["minecraft:time_since_rest"] is null && this.TimeSinceRest > 0)
            this.Statistics.Increment("minecraft:time_since_rest", this.TimeSinceRest);

        this.progressDead = false;
        this.ProgressAdvancements.Load(ProgressFiles.Read(this.ProgressPath("advancements", this.loadedUuid), this.Logger));
        this.progressDimension = this.Level.DimensionName;
        this.progressInventory = null;
        this.TimeSinceRest = this.Statistics.Get("minecraft:time_since_rest");
    }

    private async Task SaveProgressAsync()
    {
        await ProgressFiles.WriteAsync(this.ProgressPath("stats", this.Uuid), this.ProgressStatistics.Save());
        await ProgressFiles.WriteAsync(this.ProgressPath("advancements", this.Uuid), this.ProgressAdvancements.Save());
    }

    private async ValueTask TickProgressAsync()
    {
        this.Statistics.Increment("minecraft:play_time");
        this.Statistics.Increment("minecraft:total_world_time");
        if (this.Alive)
            this.Statistics.Increment("minecraft:time_since_death");

        if (this.Sleeping)
            this.ProgressStatistics.Reset("minecraft:time_since_rest");
        else
            this.Statistics.Increment("minecraft:time_since_rest");

        if (this.Sneaking)
            this.Statistics.Increment("minecraft:sneak_time");

        if (!this.Alive || this.Respawning)
            return;

        this.progressDead = false;

        while (this.craftedRecipes.TryDequeue(out var recipe))
            await this.FireProgressAsync("recipe_crafted", recipe: recipe);

        await this.FireProgressAsync("tick");
        if (this.Level.LevelData.Time % 20 == 0)
            await this.FireProgressAsync("location");

        if (this.progressDimension is string previous && previous != this.Level.DimensionName)
            await this.FireProgressAsync("changed_dimension", from: previous, to: this.Level.DimensionName);

        this.progressDimension = this.Level.DimensionName;
        if (this.Sleeping && !this.progressSleeping)
            await this.FireProgressAsync("slept_in_bed");

        this.progressSleeping = this.Sleeping;
        bool inventoryChanged;
        using (await this.Inventory.LockAsync())
        {
            var items = this.CaptureInventoryItems();
            inventoryChanged = this.progressInventory is null || !items.SequenceEqual(this.progressInventory);
            this.progressInventory = items;
        }

        if (inventoryChanged)
            await this.FireProgressAsync("inventory_changed");
    }

    // Grants take tickGate to keep criteria and rewards together with saves and leaving. A death must finish
    // before waiting for that gate: a tick holding it may itself be waiting for the death before respawning.
    // Other triggers fired from inventory/chunk transactions follow the same lock order.
    ValueTask IPlayer.FireAdvancementTriggerAsync(string trigger, Func<JsonElement, bool> matches) =>
        this.ProgressAdvancements.FireAsync(trigger, matches);

    ValueTask IPlayer.FlushAdvancementsAsync() => this.ProgressAdvancements.FlushAsync();

    internal ValueTask FireProgressAsync(string trigger, ItemStack? item = null, IEntity? entity = null,
        IBlock? block = null, Vector? position = null, string? recipe = null, string? from = null,
        string? to = null, int? levels = null) => DeferredInventoryWork.RunOrDeferAsync(
            () => this.ProgressAdvancements.FireAsync(
                $"minecraft:{trigger}",
                conditions => ProgressConditions.Match(
                    this, trigger, conditions, item, entity, block, position, recipe, from, to, levels)));

    /// <summary>Record only accepted movement. Vanilla's distance stats round blocks to centimetres.</summary>
    private void RecordProgressMovement(VectorD previous, MovementFlags previousFlags)
    {
        if (this.Vehicle is not null)
            return;

        var delta = this.Position - previous;
        var horizontal = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        var grounded = this.MovementFlags.HasFlag(MovementFlags.OnGround);
        var terrain = new AI.MobTerrain(this.Level);
        var feet = terrain.GetBlock((Vector)this.Position.Floor());
        var eyes = terrain.GetBlock((Vector)EyePosition(this).Floor());
        var underwater = eyes?.Is(BlockIds.Water) == true;
        var inWater = feet?.Is(BlockIds.Water) == true;
        var climbing = feet is not null && TagsRegistry.Block.Climbable.Entries.Contains(feet.RegistryId);
        var stat = this.Swimming ? "swim_one_cm"
            : underwater ? "walk_under_water_one_cm"
            : inWater ? "walk_on_water_one_cm"
            : climbing && delta.Y > 0 ? "climb_one_cm"
            : grounded ? this.Sprinting ? "sprint_one_cm" : this.Sneaking ? "crouch_one_cm" : "walk_one_cm"
            : this.FlyingWithElytra ? "aviate_one_cm"
            : Centimetres(horizontal) > 25 ? "fly_one_cm"
            : null;
        if (stat is not null)
        {
            var distance = climbing ? Math.Max(0, delta.Y)
                : this.Swimming || underwater || this.FlyingWithElytra ? delta.Magnitude
                : horizontal;
            this.Statistics.Increment($"minecraft:{stat}", Centimetres(distance));
        }

        if (previousFlags.HasFlag(MovementFlags.OnGround) && !grounded && delta.Y > 0)
            this.Statistics.Increment("minecraft:jump");
    }

    internal static int Centimetres(double blocks) => (int)Math.Clamp(Math.Floor(blocks * 100 + 0.5), 0, int.MaxValue);

    internal void RecordProgressDamage(IEntity source, float amount)
    {
        var points = (int)Math.Clamp(Math.Floor(amount * 10 + 0.5), 0, int.MaxValue);
        this.Statistics.Increment("minecraft:damage_taken", points);
        if (source is IPlayer attacker && !ReferenceEquals(source, this))
            attacker.Statistics.Increment("minecraft:damage_dealt",
                (int)Math.Clamp(Math.Floor(Math.Min(this.Health, amount) * 10 + 0.5), 0, int.MaxValue));
    }

    internal async ValueTask RecordProgressDeathAsync(IEntity source)
    {
        if (this.progressDead)
            return;

        this.progressDead = true;
        this.Statistics.Increment("minecraft:deaths");
        this.ProgressStatistics.Reset("minecraft:time_since_death");
        var identifier = ProgressConditions.EntityIdentifier(source);
        if (identifier is not null && !ReferenceEquals(source, this))
            this.Statistics.Increment(identifier, type: "minecraft:killed_by");

        if (!ReferenceEquals(source, this))
            await this.FireProgressAsync("entity_killed_player", entity: source);
        if (source is IPlayer killer && !ReferenceEquals(source, this))
            await killer.RecordProgressKillAsync(this);
    }

    internal async ValueTask RecordProgressKillAsync(IEntity victim)
    {
        var identifier = ProgressConditions.EntityIdentifier(victim);
        if (identifier is not null)
            this.Statistics.Increment(identifier, type: "minecraft:killed");

        this.Statistics.Increment(victim is IPlayer ? "minecraft:player_kills" : "minecraft:mob_kills");
        await this.FireProgressAsync("player_killed_entity", entity: victim);
    }

    public async ValueTask GiveExperienceAsync(int points)
    {
        if (points <= 0)
            return;

        this.XpTotal = (int)Math.Min(int.MaxValue, (long)this.XpTotal + points);
        var progress = this.XpP * XpHelper.ExperienceRequiredForNextLevel(this.XpLevel) + points;
        while (progress >= XpHelper.ExperienceRequiredForNextLevel(this.XpLevel))
        {
            progress -= XpHelper.ExperienceRequiredForNextLevel(this.XpLevel);
            this.XpLevel++;
        }

        this.XpP = progress / XpHelper.ExperienceRequiredForNextLevel(this.XpLevel);
        await this.QueuePacketAsync(new SetExperiencePacket(this.XpP, this.XpLevel, this.XpTotal));
    }

    internal async ValueTask RewardAdvancementAsync(Advancement advancement)
    {
        using var host = Plugins.PluginCall.EnterHost();
        if (advancement.Reward is not AdvancementReward rewards)
            return;

        await this.GiveExperienceAsync(rewards.Experience ?? 0);

        foreach (var identifier in rewards.Loot)
        {
            if (PluginLootTables.Resolve(identifier) is not LootTable table)
                continue;

            var context = new LootContext
            {
                Random = table.CreateRandom(0),
                ThisEntity = this,
                Origin = this.Position,
                Luck = this.GetAttributeValue("minecraft:luck"),
                ResolveTable = PluginLootTables.CaptureResolver()
            };
            var items = table.GetRandomItems(context);

            // Granted by plugins' code holding the inventory lock, the items come as soon as the lock is let go.
            await DeferredInventoryWork.RunOrDeferAsync(() => this.GiveRewardItemsAsync(items));
        }

        foreach (var identifier in rewards.Recipes)
            await this.UnlockProgressRecipeAsync(identifier);
    }

    // Puts an advancement's loot in the inventory, dropping what doesn't fit.
    private async ValueTask GiveRewardItemsAsync(List<ItemStack> items)
    {
        List<ClientboundPacket> updates;
        ClickWindowState state;

        using (await this.Inventory.LockAsync())
        {
            var before = this.CaptureInventoryItems();
            foreach (var item in items)
                AddToInventory(this.Inventory, this.CurrentHeldItemSlot, item, false);

            state = ClickWindowState.Of(this);
            updates = this.CaptureInventoryChanges(before);
        }

        await this.WindowSnapshots.SendChangeAsync(this, state, updates);
        foreach (var item in items.Where(item => item.Count > 0))
            ItemEntity.DropHostOwned(this, item);
    }

    internal async ValueTask UnlockProgressRecipeAsync(string identifier)
    {
        var recipes = Events.MainEventHandler.PlayerRecipes(this);
        if ((!recipes.Recipes.ContainsKey(identifier)
                && !recipes.Recipes.ContainsKey(identifier.TrimResourceTag(keepUnderscores: true)))
            || !this.unlockedRecipes.Add(identifier))
            return;

        // Obsidian currently exposes every crafting recipe. A reward still explicitly adds its display and
        // records the vanilla recipe_book identity, so it remains known when selective unlocking is implemented.
        var entries = recipes.Book
            .Where(entry => entry is not null
                && AdvancementsRegistry.Qualify(entry.Recipe.Identifier) == identifier)
            .Select(entry => entry!).ToArray();
        if (entries.Length > 0)
            await this.QueuePacketAsync(new RecipeBookAddPacket(entries, replace: false));

        await this.FireProgressAsync("recipe_unlocked", recipe: identifier);
    }
}
