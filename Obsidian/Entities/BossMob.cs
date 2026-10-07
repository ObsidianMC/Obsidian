using Obsidian.API.Boss;

namespace Obsidian.Entities;

public abstract class BossMob : PathfinderMob
{
    private BossBar? bar;
    private readonly HashSet<int> nearbyPlayers = [];
    private readonly List<int> removedPlayers = [];
    protected virtual BossBarColor BarColor => BossBarColor.Purple;
    protected virtual BossBarFlags BarFlags => BossBarFlags.DarkenSky;
    protected virtual float BarProgress => Health / Math.Max(1, GetAttributeValue("minecraft:generic.max_health"));
    protected override bool CanDespawn => false;

    public override async ValueTask TickAsync()
    {
        await base.TickAsync();
        if (IsRemoved || !Alive) { ClearBar(); return; }
        bar ??= new BossBar(PacketBroadcaster, CustomName ?? new ChatMessage { Translate = TranslationKey },
            Math.Clamp(BarProgress, 0, 1), BarColor, BossBarDivisionType.None, BarFlags);
        nearbyPlayers.Clear();
        foreach (var player in Level.GetPlayersInRange(Position, 128)) nearbyPlayers.Add(player.EntityId);
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
