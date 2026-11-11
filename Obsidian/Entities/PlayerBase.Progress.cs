using Microsoft.Extensions.Logging;
using Obsidian.API.Advancements;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.Progress;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Progress;
using Obsidian.WorldData;
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

    // Recipes the player is given or crafted, given out under the tick gate by the player's progress tick, or by their
    // leave before the final save. Nothing is queued once the leave has given out the last of them (recipeEventsClosed),
    // which recipeEventsSync keeps atomic with queueing; it's never held across an await.
    private readonly ConcurrentQueue<RecipeEvent> recipeEvents = new();
    private readonly object recipeEventsSync = new();
    private bool recipeEventsClosed;

    // A recipe the player is given (Unlocks: recorded as known, firing recipe_unlocked if it wasn't) or crafted
    // (firing recipe_crafted).
    private readonly record struct RecipeEvent(string Recipe, bool Unlocks);

    /// <summary>Applies criteria and all their rewards as one player state change, unless the player left.</summary>
    /// <exception cref="InvalidOperationException">The current flow holds another player's tick gate.</exception>
    internal async ValueTask<bool> GrantAdvancementAsync(Func<ValueTask<bool>> grant)
    {
        this.ThrowIfInAnotherPlayersTick("Granting an advancement");

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
        this.TryQueueRecipeEvents([new RecipeEvent(AdvancementsRegistry.Qualify(identifier), Unlocks: false)]);
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

        await this.GiveQueuedRecipesAsync();

        await this.FireProgressAsync("tick");
        if (this.Level.LevelData.GameTime % 20 == 0)
            await this.FireProgressAsync("location");

        if (this.progressDimension is string previous && previous != this.Level.DimensionName)
            await this.FireProgressAsync("changed_dimension", from: previous, to: this.Level.DimensionName);

        this.progressDimension = this.Level.DimensionName;
        // Vanilla's SLEPT_IN_BED fires only for a bed that set the spawn: never for a straw bed.
        if (this.Sleeping && !this.progressSleeping && this.SleepSetSpawn)
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
    ValueTask IPlayer.FireAdvancementTriggerAsync(string trigger, Func<JsonElement, bool> matches)
    {
        // A plugin's trigger is refused at once, rather than only once one of its criteria matches.
        this.ThrowIfInAnotherPlayersTick("Firing an advancement trigger");
        return this.ProgressAdvancements.FireAsync(trigger, matches);
    }

    ValueTask IPlayer.FlushAdvancementsAsync() => this.ProgressAdvancements.FlushAsync();

    internal ValueTask FireProgressAsync(string trigger, ItemStack? item = null, IEntity? entity = null,
        IBlock? block = null, Vector? position = null, string? recipe = null, string? from = null,
        string? to = null, int? levels = null, string? lootTable = null)
    {
        ValueTask Fire() =>
            this.FireProgressNowAsync(trigger, item, entity, block, position, recipe, from, to, levels, lootTable);

        // Gameplay firing a trigger for this player from another's tick (a hit landing in it, say) can't wait for this
        // player's gate. The event is matched now, as it is (the killer's position and dimension, say), and what it
        // matched is granted in the server's background work once the ticks let go of the gate, which shutdown waits
        // for before the players leave. A player leaving meanwhile has their grants stopped, so theirs is dropped, as
        // a trigger fired after the leave would be.
        if (this.HoldsAnotherPlayersGate)
        {
            var matched = this.ProgressAdvancements.Match(
                $"minecraft:{trigger}",
                conditions => ProgressConditions.Match(
                    this, trigger, conditions, item, entity, block, position, recipe, from, to, levels, lootTable: lootTable));
            if (matched.Count > 0 && this.Server is Server server)
                server.BackgroundWork.TryStart(() => this.GrantMatchedAsync(matched));

            return ValueTask.CompletedTask;
        }

        return DeferredInventoryWork.RunOrDeferAsync(Fire);
    }

    /// <summary>
    /// Fires a trigger for this player from a level's tick, which mustn't wait for this player's tick gate or
    /// inventory: the event is matched now, as it is, and what it matched is granted in the server's background work
    /// once the ticks let go of the gates (as for a trigger fired from another player's tick).
    /// </summary>
    /// <param name="trigger">The trigger, without its namespace.</param>
    /// <param name="entity">The entity the trigger's conditions test: <c>target_hit</c>'s projectile, say.</param>
    /// <param name="signal"><c>target_hit</c>'s signal strength.</param>
    internal void QueueProgress(string trigger, IEntity? entity = null, int? signal = null)
    {
        var matched = this.ProgressAdvancements.Match(
            $"minecraft:{trigger}",
            conditions => ProgressConditions.Match(
                this, trigger, conditions, null, entity, null, null, null, null, null, null, signal));
        if (matched.Count > 0 && this.Server is Server server)
            server.BackgroundWork.TryStart(() => this.GrantMatchedAsync(matched));
    }

    /// <summary>
    /// Matches a trigger's criteria against the world as it is now, for a use that changes the world before it can grant
    /// them: vanilla fires some triggers before its write (honeycomb's first <c>item_used_on_block</c>, say), so their
    /// location checks see the block as it was. <see cref="GrantProgressAsync"/> grants what matched once the change
    /// committed.
    /// </summary>
    internal List<(string Advancement, string Criterion)> MatchProgress(string trigger, ItemStack? item = null,
        IBlock? block = null, Vector? position = null) =>
        this.ProgressAdvancements.Match(
            $"minecraft:{trigger}",
            conditions => ProgressConditions.Match(
                this,
                trigger,
                conditions,
                item,
                entity: null,
                block,
                position,
                recipe: null,
                from: null,
                to: null,
                levels: null));

    /// <summary>
    /// Grants the criteria <see cref="MatchProgress"/> matched, the way <see cref="FireProgressAsync"/> grants them.
    /// </summary>
    internal ValueTask GrantProgressAsync(List<(string Advancement, string Criterion)> matched)
    {
        if (matched.Count == 0)
            return ValueTask.CompletedTask;

        if (this.HoldsAnotherPlayersGate)
        {
            if (this.Server is Server server)
                server.BackgroundWork.TryStart(() => this.GrantMatchedAsync(matched));

            return ValueTask.CompletedTask;
        }

        return DeferredInventoryWork.RunOrDeferAsync(() => new ValueTask(this.GrantMatchedAsync(matched)));
    }

    // Fires the trigger at once, for a flow that holds no inventory lock: one under the player's own tick gate, say.
    private ValueTask FireProgressNowAsync(string trigger, ItemStack? item = null, IEntity? entity = null,
        IBlock? block = null, Vector? position = null, string? recipe = null, string? from = null,
        string? to = null, int? levels = null, string? lootTable = null) =>
        this.ProgressAdvancements.FireAsync(
            $"minecraft:{trigger}",
            conditions => ProgressConditions.Match(
                this, trigger, conditions, item, entity, block, position, recipe, from, to, levels, lootTable: lootTable));

    private async Task GrantMatchedAsync(List<(string Advancement, string Criterion)> matched)
    {
        foreach (var (advancement, criterion) in matched)
            await this.ProgressAdvancements.GrantAsync(advancement, criterion);
    }

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
        {
            attacker.Statistics.Increment("minecraft:damage_dealt",
                (int)Math.Clamp(Math.Floor(Math.Min(this.Health, amount) * 10 + 0.5), 0, int.MaxValue));
        }
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
                EntityVillagerVariant = LootEntityComponents.VillagerVariant,
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

    /// <summary>
    /// Takes what <paramref name="machine"/> cooked for the player taking its result (vanilla's
    /// <c>awardUsedRecipesAndPopExperience</c>), and queues the recipes for the player's next progress tick: in
    /// vanilla's order, those they don't know join their book, each firing <c>recipe_unlocked</c>, and then
    /// <c>recipe_crafted</c> fires for every one. The caller holds the machine's inventory lock and drops the
    /// returned experience.
    /// </summary>
    /// <returns>
    /// The recipes taken, with their experience; null, taking nothing, once the player has left, so that the machine
    /// keeps them for whoever takes its result next.
    /// </returns>
    internal List<FurnaceBlockEntity.CookedRecipe>? TakeCookedRecipes(
        FurnaceBlockEntity machine,
        RecipeSet recipes,
        Func<float> random)
    {
        lock (this.recipeEventsSync)
        {
            if (this.recipeEventsClosed)
                return null;

            var cooked = machine.TakeCookedRecipes(recipes, random);
            var identifiers = cooked.Select(recipe => AdvancementsRegistry.Qualify(recipe.Recipe.Identifier)).ToList();
            foreach (var identifier in identifiers)
                this.recipeEvents.Enqueue(new RecipeEvent(identifier, Unlocks: true));

            foreach (var identifier in identifiers)
                this.recipeEvents.Enqueue(new RecipeEvent(identifier, Unlocks: false));

            return cooked;
        }
    }

    // False once the player has left: what they crafted then is never fired.
    private bool TryQueueRecipeEvents(IEnumerable<RecipeEvent> events)
    {
        lock (this.recipeEventsSync)
        {
            if (this.recipeEventsClosed)
                return false;

            foreach (var recipeEvent in events)
                this.recipeEvents.Enqueue(recipeEvent);

            return true;
        }
    }

    // Under the tick gate. Each recipe is handled on its own: one that throws is logged and the rest are still given,
    // and none leaves the queue before it was handled, so a save that runs after it sees it given.
    private async ValueTask GiveQueuedRecipesAsync()
    {
        while (this.recipeEvents.TryPeek(out var next))
        {
            try
            {
                if (next.Unlocks)
                    await this.UnlockProgressRecipeAsync(next.Recipe);
                else
                    await this.FireProgressNowAsync("recipe_crafted", recipe: next.Recipe);
            }
            catch (Exception exception)
            {
                RecipeEventFailed(this.Logger, exception, next.Recipe);
            }
            finally
            {
                this.recipeEvents.TryDequeue(out _);
            }
        }
    }

    // Under the tick gate, as the player leaves: gives out what's queued, then closes the queue once it's empty, so
    // that nothing queued afterwards waits for a tick that won't come.
    private async ValueTask GiveQueuedRecipesAndCloseAsync()
    {
        while (true)
        {
            await this.GiveQueuedRecipesAsync();
            lock (this.recipeEventsSync)
            {
                if (this.recipeEvents.IsEmpty)
                {
                    this.recipeEventsClosed = true;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Adds the recipe to the player's book, if the server has it and they don't know it yet, and fires
    /// <c>recipe_unlocked</c> for it at once. Call it under the player's tick gate, as advancement rewards and the
    /// player's progress tick do, so that a save sees the recipe known together with what the trigger met. If the
    /// trigger throws, the recipe is forgotten again, so it's never known without its trigger having fired.
    /// </summary>
    internal async ValueTask UnlockProgressRecipeAsync(string identifier)
    {
        if (!await this.AddKnownRecipeAsync(identifier))
            return;

        try
        {
            await this.FireProgressNowAsync("recipe_unlocked", recipe: identifier);
        }
        catch
        {
            this.unlockedRecipes.Remove(identifier);
            throw;
        }
    }

    // Records the recipe as known and shows it in the book; false when the server doesn't have it or it's known.
    private async ValueTask<bool> AddKnownRecipeAsync(string identifier)
    {
        var recipes = Events.MainEventHandler.PlayerRecipes(this);
        if ((!recipes.Recipes.ContainsKey(identifier)
                && !recipes.Recipes.ContainsKey(identifier.TrimResourceTag(keepUnderscores: true)))
            || !this.unlockedRecipes.Add(identifier))
        {
            return false;
        }

        // Obsidian currently exposes every crafting recipe. A reward still explicitly adds its display and
        // records the vanilla recipe_book identity, so it remains known when selective unlocking is implemented.
        var entries = recipes.Book
            .Where(entry => entry is not null
                && AdvancementsRegistry.Qualify(entry.Recipe.Identifier) == identifier)
            .Select(entry => entry!).ToArray();
        if (entries.Length > 0)
            await this.QueuePacketAsync(new RecipeBookAddPacket(entries, replace: false));

        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Giving the recipe {Recipe} failed")]
    private static partial void RecipeEventFailed(ILogger logger, Exception exception, string recipe);
}
