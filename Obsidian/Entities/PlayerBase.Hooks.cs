using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Plugins;

namespace Obsidian.Entities;

// IPlayer's internal gameplay hooks, which gameplay code calls rather than testing for this type. An internal interface
// member is only implemented explicitly: a member of the same name and signature doesn't implement it, and the
// interface's default would run instead, so each forwards to the member that does the work.
public partial class PlayerBase
{
    bool IPlayer.Connected => this.Connected;

    bool IPlayer.CanAttack(IEntity target, ItemStack? item) => this.CanAttack(target, item);
    ValueTask IPlayer.ResolveAttackAsync(PlayerAttackEntityEventArgs attack) => this.ResolveAttackAsync(attack);

    ValueTask<bool> IPlayer.ReceiveProjectileAsync(ItemStack item, bool creativeOnly) =>
        this.ReceiveProjectileAsync(item, creativeOnly);
    ValueTask<bool> IPlayer.PickUpExperienceAsync(int experience) => this.PickUpExperienceAsync(experience);

    ValueTask IPlayer.SyncInventorySlotAsync(int slot) => this.SyncInventorySlotAsync(slot);
    void IPlayer.RunMenusClosed() => this.RunMenusClosed();
    ValueTask IPlayer.ClickContainerButtonAsync(int containerId, int button) =>
        this.ClickContainerButtonAsync(containerId, button);
    bool IPlayer.CountCreativeDrop() => this.CountCreativeDrop();

    void IPlayer.StartEating(InteractionHand hand) => this.StartEating(hand);
    async ValueTask<bool> IPlayer.RunItemUseAsync(Item item, Func<ValueTask<bool>> use)
    {
        var used = await this.RunItemUseAsync(use);
        if (used)
            this.Statistics.Increment(item.UnlocalizedName, type: "minecraft:used");

        return used;
    }

    void IPlayer.TrackJumpInput(PlayerInput previous, PlayerInput current) => this.TrackJumpInput(previous, current);
    void IPlayer.ReleaseMovementAbilities(IPluginContainer plugin) => this.ReleaseMovementAbilities(plugin);

    Task IPlayer.CancelPortalTravelAsync() => this.CancelPortalTravelAsync();
    Task IPlayer.TransferDimensionAsync(ILevel destination, VectorD? position, Angle? yaw, Angle? pitch,
        VectorD? motion, bool portal, bool relativeMotion) =>
        this.TransferDimensionAsync(destination, position, yaw, pitch, motion, portal, relativeMotion);

    ValueTask IPlayer.FireProgressAsync(string trigger, ItemStack? item, IEntity? entity, IBlock? block,
        Vector? position, string? recipe, string? from, string? to, int? levels) =>
        this.FireProgressAsync(trigger, item, entity, block, position, recipe, from, to, levels);
    ValueTask<bool> IPlayer.GrantAdvancementAsync(Func<ValueTask<bool>> grant) => this.GrantAdvancementAsync(grant);
    ValueTask IPlayer.RewardAdvancementAsync(API.Advancements.Advancement advancement) =>
        this.RewardAdvancementAsync(advancement);
}
