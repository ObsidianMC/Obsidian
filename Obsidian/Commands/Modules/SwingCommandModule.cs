using Obsidian.Commands.Framework;
using Obsidian.API.Commands;
using Obsidian.API.Inventory;
using Obsidian.Entities;

namespace Obsidian.Commands.Modules;

[RequirePermissionLevel(2)]
public sealed class SwingCommandModule : CommandModuleBase
{
    [Command("swing")]
    public Task<int> SwingAsync() => this.RunAsync(new CommandInput("@s"));

    [CommandOverload]
    public Task<int> SwingAsync([Remaining] string arguments) =>
        this.RunAsync(CommandInput.Arguments(this.CommandContext));

    private async Task<int> RunAsync(CommandInput input)
    {
        try
        {
            if (!EntitySelector.TryParse(input.Read(), out var selector))
                throw new FormatException("Invalid entity selector");

            var targets = selector.Select(this.CommandContext);
            if (targets.Count == 0)
                throw new FormatException("No entity was found");

            var handName = input.End ? "mainhand" : input.Read();
            var hand = handName switch
            {
                "mainhand" => InteractionHand.MainHand,
                "offhand" => InteractionHand.OffHand,
                _ => throw new FormatException("Expected mainhand or offhand")
            };
            var typeName = input.End ? "whack" : input.Read();
            if (!Enum.TryParse<SwingAnimationType>(typeName, true, out var type)
                || !Enum.IsDefined(type) || int.TryParse(typeName, out _))
            {
                throw new FormatException("Unknown swing animation");
            }

            var duration = input.End ? 6 : CommandInput.Ticks(input.Read(), 1);
            input.ExpectEnd();
            var animation = new SwingAnimation { Type = type, Duration = duration };
            var count = 0;
            foreach (var target in targets.OfType<Living>())
            {
                target.Swing(hand, animation, includeSelf: true);
                count++;
            }

            if (count == 0)
                throw new FormatException("No living entity was found");

            await this.CommandContext.SuccessAsync($"Swung {count} living entities");
            return count;
        }
        catch (FormatException exception)
        {
            await this.CommandContext.FailAsync(exception.Message);
            return 0;
        }
    }
}
