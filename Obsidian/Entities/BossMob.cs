using Obsidian.API.Boss;

namespace Obsidian.Entities;

public abstract class BossMob : PathfinderMob
{
    private BossBar? bar;
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
        var players = Level.GetPlayersInRange(Position, 128).Select(player => player.EntityId).ToHashSet();
        foreach (var id in bar.Players.Where(id => !players.Contains(id)).ToArray()) bar.RemovePlayer(id);
        foreach (var id in players) bar.AddPlayer(id);
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
