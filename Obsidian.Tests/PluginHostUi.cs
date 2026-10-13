using Obsidian.API;
using Obsidian.API.Boss;
using Obsidian.API.World;
using Obsidian.Net.Actions.BossBar;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Scoreboard;
using Obsidian.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The scoreboards and boss bars plugins create: who their packets go to, and how unloading the plugin retires them.
/// </summary>
public sealed class PluginHostUi
{
    [Fact]
    public void RemoveObjective_GoesToThePlayersShownTheScoreboard()
    {
        var broadcaster = new Recorder();
        var scoreboard = new Scoreboard("kills", broadcaster);

        scoreboard.AddPlayer(1);
        scoreboard.AddPlayer(2);
        scoreboard.CreateOrUpdateObjective("Kills");
        scoreboard.RemoveObjective();

        var (packet, to) = broadcaster.Sent[^1];

        Assert.Equal(ScoreboardMode.Remove, Assert.IsType<SetObjectivePacket>(packet).Mode);
        Assert.Equal([1, 2], to.Order());
        Assert.Null(scoreboard.Objective);
    }

    [Fact]
    public void Retire_HidesTheScoreboardAndRefusesChanges()
    {
        var broadcaster = new Recorder();
        var scoreboard = new Scoreboard("kills", broadcaster);

        scoreboard.AddPlayer(1);
        scoreboard.CreateOrUpdateObjective("Kills");
        var team = scoreboard.CreateTeam("red", "Red", NameTagVisibility.Always, CollisionRule.Always, TeamColor.Red);

        scoreboard.Retire();

        var removal = Assert.Single(broadcaster.Sent, sent => sent.Packet is SetObjectivePacket
        {
            Mode: ScoreboardMode.Remove
        });
        Assert.Equal([1], removal.To);
        Assert.Equal(TeamModeOption.RemoveTeam, Assert.IsType<SetPlayerTeamPacket>(broadcaster.Everyone[^1]).Mode);
        Assert.Empty(scoreboard.Teams);

        // A handler still running can't show it again, but removing does nothing.
        var sent = broadcaster.Sent.Count + broadcaster.Everyone.Count;

        Assert.Throws<InvalidOperationException>(() => scoreboard.AddPlayer(2));
        Assert.Throws<InvalidOperationException>(() => scoreboard.CreateOrUpdateObjective("Kills"));
        Assert.Throws<InvalidOperationException>(() => scoreboard.CreateOrUpdateScore("a", "A", 1));
        Assert.Throws<InvalidOperationException>(() => team.AddEntities("Steve"));
        Assert.Throws<InvalidOperationException>(() => scoreboard.CreateTeam(
            "blue",
            "Blue",
            NameTagVisibility.Always,
            CollisionRule.Always,
            TeamColor.Blue));
        Assert.False(scoreboard.RemovePlayer(1));
        team.Delete();

        Assert.Equal(sent, broadcaster.Sent.Count + broadcaster.Everyone.Count);
    }

    [Fact]
    public void Display_ThenRetire_QueuesTheRemovalAfterTheDisplay()
    {
        var broadcaster = new Recorder();
        var scoreboard = new Scoreboard("kills", broadcaster);

        scoreboard.CreateOrUpdateObjective("Kills");
        scoreboard.AddPlayer(1);

        // Displays and removals share the broadcaster's queue, so the player gets them in the order they happened.
        scoreboard.Display(1, DisplaySlot.Sidebar);
        scoreboard.Retire();

        var packets = broadcaster.Sent
            .Where(sent => sent.To.SequenceEqual([1]))
            .Select(sent => sent.Packet)
            .ToList();

        Assert.Equal(ScoreboardMode.Create, Assert.IsType<SetObjectivePacket>(packets[0]).Mode);
        Assert.IsType<SetDisplayObjectivePacket>(packets[^2]);
        Assert.Equal(ScoreboardMode.Remove, Assert.IsType<SetObjectivePacket>(packets[^1]).Mode);
        Assert.Throws<InvalidOperationException>(() => scoreboard.Display(1, DisplaySlot.Sidebar));
    }

    [Fact]
    public void RetireBossBars_HidesShownBarsThePluginDropped()
    {
        var broadcaster = new Recorder();
        var bars = new PluginBossBars();

        // Shown, and no longer referenced but by the plugin's bars.
        var uuid = ShowDroppedBar(bars, broadcaster);
        var kept = NewBar(broadcaster);
        Assert.True(bars.TryAdd(kept));

        GC.Collect();
        GC.WaitForPendingFinalizers();

        bars.Retire();

        var (packet, to) = broadcaster.Sent[^1];
        var removal = Assert.IsType<BossBarRemoveAction>(Assert.IsType<BossEventPacket>(packet).Action);
        Assert.Equal(uuid, removal.Uuid);
        Assert.Equal([7], to);

        // The plugin's bars refuse changes, and it adds no more.
        Assert.Throws<InvalidOperationException>(() => kept.AddPlayer(7));
        Assert.Throws<InvalidOperationException>(() => kept.UpdateTitle("Again"));
        Assert.False(bars.TryAdd(NewBar(broadcaster)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Guid ShowDroppedBar(PluginBossBars bars, Recorder broadcaster)
    {
        var bar = NewBar(broadcaster);

        Assert.True(bars.TryAdd(bar));
        bar.AddPlayer(7);

        return bar.Uuid;
    }

    private static BossBar NewBar(IPacketBroadcaster broadcaster) =>
        new(broadcaster, "Boss", 1, BossBarColor.Red, BossBarDivisionType.None, BossBarFlags.None);

    // Records packets sent to given players, and those sent to everyone (less some).
    private sealed class Recorder : IPacketBroadcaster
    {
        public List<(IClientboundPacket Packet, int[] To)> Sent { get; } = [];

        public List<IClientboundPacket> Everyone { get; } = [];

        public void Broadcast(IClientboundPacket packet, params int[] excludedIds) => this.Everyone.Add(packet);

        public void BroadcastTo(IClientboundPacket packet, params int[] ids) => this.Sent.Add((packet, ids));

        public void BroadcastToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds) =>
            this.Everyone.Add(packet);

        public void BroadcastToLevelInRange(
            ILevel level,
            VectorD location,
            IClientboundPacket packet,
            params int[] excludedIds) =>
            this.Everyone.Add(packet);

        public void QueuePacketTo(IClientboundPacket packet, params int[] ids) => this.Sent.Add((packet, ids));

        public void QueuePacketTo(IClientboundPacket packet, int priority, params int[] ids) =>
            this.Sent.Add((packet, ids));

        public void QueuePacketToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds) =>
            this.Everyone.Add(packet);

        public void QueuePacketToLevelInRange(
            ILevel level,
            VectorD location,
            IClientboundPacket packet,
            params int[] excludedIds) =>
            this.Everyone.Add(packet);

        public void QueuePacket(IClientboundPacket packet, params int[] excludedIds) => this.Everyone.Add(packet);

        public void QueuePacketToLevel(
            ILevel toLevel,
            int priority,
            IClientboundPacket packet,
            params int[] excludedIds) =>
            this.Everyone.Add(packet);

        public void QueuePacket(IClientboundPacket packet, int priority, params int[] excludedIds) =>
            this.Everyone.Add(packet);
    }
}
