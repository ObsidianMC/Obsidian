using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Net;
using System.Threading;

namespace Obsidian.WorldData.TrialSpawners;

/// <summary>
/// A vault's block entity, like vanilla's <c>VaultBlockEntity</c>: its config, what it shares with clients, and what
/// only the server knows. Its tick connects players, cycles the display and ejects an unlocked reward.
/// </summary>
/// <remarks>
/// Saved as vanilla saves it: <c>config</c>, <c>shared_data</c> and <c>server_data</c>, always written, each field in
/// them left out at its default. A <c>config</c> that doesn't read (activation beyond deactivation range, say) is kept
/// as loaded, and the vault uses the default config. Other fields are kept as loaded.
/// </remarks>
internal sealed partial class VaultBlockEntity : BlockEntity
{
    private const string ConfigField = "config";
    private const string SharedField = "shared_data";
    private const string ServerField = "server_data";

    private VaultData data = VaultData.Empty;

    internal override bool SavesOnAnyThread => true;

    /// <summary>The saved data as of the last load or change.</summary>
    public VaultData Data
    {
        get => Volatile.Read(ref this.data);
        set => Volatile.Write(ref this.data, value);
    }

    protected internal override void Load(NbtCompound tag)
    {
        var others = new NbtCompound();
        foreach (var (name, child) in tag)
        {
            if (name is not (ConfigField or SharedField or ServerField))
                others.Add(name, DataBlockEntity.Copy(child));
        }

        var config = VaultConfig.Default;
        if (tag.TryGetTag<NbtCompound>(ConfigField, out var savedConfig))
        {
            if (VaultConfig.Read(savedConfig) is VaultConfig read)
                config = read;
            else
                others.Add(ConfigField, DataBlockEntity.Copy(savedConfig));
        }

        var sharedData = tag.TryGetTag<NbtCompound>(SharedField, out var shared)
            ? VaultSharedData.Read(shared)
            : new VaultSharedData();
        var serverData = tag.TryGetTag<NbtCompound>(ServerField, out var server)
            ? VaultServerData.Read(server)
            : new VaultServerData();

        // Vanilla's VaultServerData.set restores the queue and timer but retains the live ejection total. On a
        // fresh load it is zero, which also changes the pitch progression of an interrupted ejection.
        serverData = serverData with { TotalEjectionsNeeded = this.Data.Server.TotalEjectionsNeeded };
        this.Data = new VaultData(config, sharedData, serverData, others);
    }

    protected internal override void Save(NbtCompound tag)
    {
        var saved = this.Data;
        foreach (var (name, child) in saved.Others)
            tag.Add(name, DataBlockEntity.Copy(child));

        if (!tag.HasTag(ConfigField))
            tag.Add(saved.Config.Write(ConfigField));

        tag.Add(saved.Shared.Write(SharedField));
        tag.Add(saved.Server.Write(ServerField));
    }

    // Vanilla's getUpdateTag: only the shared data, which clients draw the vault with.
    protected internal override NbtCompound? GetUpdateData()
    {
        var update = new NbtCompound { this.Data.Shared.Write(SharedField) };
        NetworkBuffer.ProjectUpdateItems(update);
        return update;
    }
}

/// <summary>Everything a vault saves. Immutable; replace it to change the vault.</summary>
/// <param name="Others">The saved fields the vault doesn't read, kept as loaded.</param>
internal sealed record VaultData(VaultConfig Config, VaultSharedData Shared, VaultServerData Server, NbtCompound Others)
{
    public static VaultData Empty { get; } =
        new(VaultConfig.Default, new VaultSharedData(), new VaultServerData(), new());
}

/// <summary>
/// How a vault opens and what it gives, like vanilla's <c>VaultConfig</c>.
/// </summary>
/// <param name="LootTable">The table its reward rolls from.</param>
/// <param name="ActivationRange">How close a player brings it to life.</param>
/// <param name="DeactivationRange">How far players must go for it to go back to sleep.</param>
/// <param name="KeyItem">The item that opens it, or null for none.</param>
/// <param name="OverrideLootTableToDisplay">The table it shows items from instead of its own, if any.</param>
internal sealed record VaultConfig(
    string LootTable,
    double ActivationRange,
    double DeactivationRange,
    ItemStack? KeyItem,
    string? OverrideLootTableToDisplay)
{
    private const string DefaultLootTable = "minecraft:chests/trial_chambers/reward";
    private const double DefaultActivationRange = 4.0;
    private const double DefaultDeactivationRange = 4.5;

    /// <summary>A vault saved without a config: the trial chambers' reward, opened by a trial key.</summary>
    public static VaultConfig Default { get; } = new(
        DefaultLootTable,
        DefaultActivationRange,
        DefaultDeactivationRange,
        new ItemStack(ItemsRegistry.Get("minecraft:trial_key")),
        null);

    /// <summary>
    /// A saved config, each field lenient as vanilla's are; a missing key item is no key item, unlike a missing
    /// config's.
    /// </summary>
    /// <returns>Null when the activation range is beyond the deactivation range, which vanilla rejects.</returns>
    public static VaultConfig? Read(NbtCompound tag)
    {
        var config = new VaultConfig(
            tag.TryGetTagValue<string>("loot_table", out var table) ? table : DefaultLootTable,
            tag.TryGetNumber<double>("activation_range", out var activation) ? activation : DefaultActivationRange,
            tag.TryGetNumber<double>("deactivation_range", out var deactivation)
                ? deactivation
                : DefaultDeactivationRange,
            ReadItem(tag, "key_item"),
            tag.TryGetTagValue<string>("override_loot_table_to_display", out var display) ? display : null);

        return config.ActivationRange > config.DeactivationRange ? null : config;
    }

    public NbtCompound Write(string name)
    {
        var tag = new NbtCompound(name);
        if (this.LootTable != DefaultLootTable)
            tag.Add(new NbtTag<string>("loot_table", this.LootTable));

        if (this.ActivationRange != DefaultActivationRange)
            tag.Add(new NbtTag<double>("activation_range", this.ActivationRange));

        if (this.DeactivationRange != DefaultDeactivationRange)
            tag.Add(new NbtTag<double>("deactivation_range", this.DeactivationRange));

        if (this.KeyItem is { IsAir: false, Count: > 0 } key)
            tag.Add(key.ToNbt("key_item"));

        if (this.OverrideLootTableToDisplay is string display)
            tag.Add(new NbtTag<string>("override_loot_table_to_display", display));

        return tag;
    }

    /// <summary>
    /// A saved stack, or null when there's none or it doesn't read (vanilla's lenient optional item).
    /// </summary>
    internal static ItemStack? ReadItem(NbtCompound tag, string name) =>
        tag.TryGetTag<NbtCompound>(name, out var item) && item.ItemFromNbt() is { IsAir: false, Count: > 0 } stack
            ? stack
            : null;
}

/// <summary>
/// What a vault shows its players, like vanilla's <c>VaultSharedData</c>, which clients get.
/// </summary>
/// <param name="DisplayItem">The item cycling inside, or null.</param>
/// <param name="ConnectedPlayers">The nearby eligible players, in their saved set's iteration order.</param>
/// <param name="ConnectedParticlesRange">How far its particles reach for them.</param>
internal sealed record VaultSharedData(
    ItemStack? DisplayItem,
    ImmutableArray<Guid> ConnectedPlayers,
    double ConnectedParticlesRange)
{
    private const double DefaultParticlesRange = 4.5;

    public VaultSharedData()
        : this(null, [], DefaultParticlesRange)
    {
    }

    public static VaultSharedData Read(NbtCompound tag) => new(
        VaultConfig.ReadItem(tag, "display_item"),
        SavedUuids.Read(tag, "connected_players"),
        tag.TryGetNumber<double>("connected_particles_range", out var range) ? range : DefaultParticlesRange);

    public NbtCompound Write(string name)
    {
        var tag = new NbtCompound(name);
        if (this.DisplayItem is { IsAir: false, Count: > 0 } display)
            tag.Add(display.ToNbt("display_item"));

        SavedUuids.Write(tag, "connected_players", this.ConnectedPlayers);
        if (this.ConnectedParticlesRange != DefaultParticlesRange)
            tag.Add(new NbtTag<double>("connected_particles_range", this.ConnectedParticlesRange));

        return tag;
    }
}

/// <summary>
/// What only the server knows of a vault, like vanilla's <c>VaultServerData</c>.
/// </summary>
/// <param name="RewardedPlayers">The players it has rewarded, who can't open it again.</param>
/// <param name="StateUpdatingResumesAt">The game time it next changes state at.</param>
/// <param name="ItemsToEject">The reward still to come out.</param>
/// <param name="TotalEjectionsNeeded">How many items the reward had.</param>
internal sealed record VaultServerData(
    ImmutableArray<Guid> RewardedPlayers,
    long StateUpdatingResumesAt,
    ImmutableArray<ItemStack> ItemsToEject,
    int TotalEjectionsNeeded)
{
    public VaultServerData()
        : this([], 0, [], 0)
    {
    }

    public static VaultServerData Read(NbtCompound tag) => new(
        SavedUuids.Read(tag, "rewarded_players"),
        tag.TryGetNumber<long>("state_updating_resumes_at", out var resumes) ? resumes : 0,
        ReadItems(tag),
        tag.TryGetNumber<int>("total_ejections_needed", out var total) ? total : 0);

    // Like vanilla's lenient list: one stack that doesn't read leaves the list empty.
    private static ImmutableArray<ItemStack> ReadItems(NbtCompound tag)
    {
        if (!tag.TryGetTag<NbtList>("items_to_eject", out var list))
            return [];

        var items = ImmutableArray.CreateBuilder<ItemStack>();
        foreach (var element in list)
        {
            if (element is not NbtCompound item || item.ItemFromNbt() is not { IsAir: false, Count: > 0 } stack)
                return [];

            items.Add(stack);
        }

        return items.ToImmutable();
    }

    public NbtCompound Write(string name)
    {
        var tag = new NbtCompound(name);
        SavedUuids.Write(tag, "rewarded_players", this.RewardedPlayers);
        if (this.StateUpdatingResumesAt != 0)
            tag.Add(new NbtTag<long>("state_updating_resumes_at", this.StateUpdatingResumesAt));

        if (!this.ItemsToEject.IsEmpty)
        {
            var items = new NbtList(NbtTagType.Compound, "items_to_eject");
            foreach (var item in this.ItemsToEject)
                items.Add(item.ToNbt());

            tag.Add(items);
        }

        if (this.TotalEjectionsNeeded != 0)
            tag.Add(new NbtTag<int>("total_ejections_needed", this.TotalEjectionsNeeded));

        return tag;
    }
}
