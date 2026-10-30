using System.Runtime.CompilerServices;

namespace Obsidian.Commands;

/// <summary>Independent vanilla tick budgets: each message costs 20, each server tick restores one.</summary>
internal static class CommandSpam
{
    private static readonly ConditionalWeakTable<IPlayer, Budget> budgets = new();

    internal sealed class Budget
    {
        private long chat;
        private long commands;

        public void Tick()
        {
            this.chat = Math.Max(0, this.chat - 1);
            this.commands = Math.Max(0, this.commands - 1);
        }

        public bool Record(bool command, int thresholdSeconds)
        {
            ref var count = ref (command ? ref this.commands : ref this.chat);
            count += 20;
            return this.UnderThreshold(command, thresholdSeconds);
        }

        public bool UnderThreshold(bool command, int thresholdSeconds) =>
            thresholdSeconds <= 0 || (command ? this.commands : this.chat) < (long)thresholdSeconds * 20;
    }

    internal static void Tick(IPlayer player)
    {
        if (budgets.TryGetValue(player, out var budget))
        {
            lock (budget)
            {
                budget.Tick();
            }
        }
    }

    internal static async ValueTask RecordAsync(IServer server, IClientPlayer player, bool command)
    {
        var threshold = command
            ? server.Configuration.CommandSpamThresholdSeconds
            : server.Configuration.ChatSpamThresholdSeconds;
        var budget = budgets.GetOrCreateValue(player);
        bool allowed;
        lock (budget)
        {
            allowed = budget.Record(command, threshold);
        }

        if (allowed || server.Operators.IsOperator(player)
            || server is Server { Integrated: { } integrated } && integrated.IsLocalPlayer(player))
        {
            return;
        }

        await player.Client.DisconnectAsync(new ChatMessage { Translate = "disconnect.spam" });
    }
}
