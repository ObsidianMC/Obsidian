using Obsidian.API.Boss;

namespace Obsidian.Entities;

public abstract class BossMob : PathfinderMob
{
    private BossBar? bar;
    private ChatMessage? defaultTitle;
    private readonly HashSet<int> nearbyPlayers = [];
    private readonly List<int> removedPlayers = [];
    protected virtual BossBarColor BarColor => BossBarColor.Purple;
    protected virtual BossBarFlags BarFlags => BossBarFlags.DarkenSky;
    protected virtual float BarProgress => Health / Math.Max(1, GetAttributeValue("minecraft:generic.max_health"));
    protected virtual bool ShowBossBar => Alive;
    protected virtual IEnumerable<IPlayer> BarPlayers => Level.Players.Values.OfType<Player>()
        .Where(player => player.TrackedEntities.TryGetValue(EntityId, out var uuid) && uuid == Uuid);
    protected override bool CanDespawn => false;

    public override async ValueTask TickAsync()
    {
        await base.TickAsync();
        if (IsRemoved || !ShowBossBar) { ClearBar(); return; }
        defaultTitle ??= new ChatMessage { Translate = TranslationKey };
        var title = CustomName ?? defaultTitle;
        bar ??= new BossBar(PacketBroadcaster, title with { },
            Math.Clamp(BarProgress, 0, 1), BarColor, BossBarDivisionType.None, BarFlags);
        if (bar.Title != title) bar.UpdateTitle(title with { });
        nearbyPlayers.Clear();
        foreach (var player in BarPlayers) nearbyPlayers.Add(player.EntityId);
        removedPlayers.Clear();
        foreach (var id in bar.Players)
            if (!nearbyPlayers.Contains(id)) removedPlayers.Add(id);
        foreach (var id in removedPlayers) bar.RemovePlayer(id);
        foreach (var id in nearbyPlayers)
            if (!bar.HasPlayer(id)) bar.AddPlayer(id);
        var progress = Math.Clamp(BarProgress, 0, 1);
        if (bar.Health != progress) bar.UpdateHealth(progress);
    }

    public override async ValueTask RemoveAsync()
    {
        ClearBar();
        await base.RemoveAsync();
    }

    private void ClearBar()
    {
        if (bar == null) return;
        foreach (var id in bar.Players.ToArray()) bar.RemovePlayer(id);
    }
}
