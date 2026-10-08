using Obsidian.API.Registry.Codecs.Dialogs;
using Obsidian.API.Builders;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public sealed partial class Player
{
    internal const string WelcomeContinueActionId = "obsidian:welcome/continue";

    internal bool IsFirstJoin { get; private set; }

    internal async ValueTask ShowFirstJoinDialogAsync()
    {
        if (!IsFirstJoin) return;
        IsFirstJoin = false;
        var feedbackEnabled = Obsidian.Server.TryGetFeedbackWebhook(Server.Configuration.FeedbackWebhookUrl, out _);
        await ShowDialogAsync(CreateWelcomeDialog(feedbackEnabled));
    }

    internal static DialogElement CreateWelcomeDialog(bool feedbackEnabled)
    {
        var builder = DialogBuilder.MultiAction(" ")
            .WithBody(DialogBody.Item("minecraft:obsidian", showDecorations: false, showTooltip: false))
            .WithMessage(new ChatMessage { Text = "Welcome to ", Bold = true }
                .AddExtra(new ChatMessage { Text = "Obsidian", Color = HexColor.DarkPurple })
                .AddExtra(new ChatMessage { Text = "!" }), width: 300)
            .WithMessage("Obsidian is still early in development. Expect bugs, missing features and changes as development continues.", width: 300)
            .WithActions(
                DialogAction.Create("GitHub", DialogClickAction.OpenUrl("https://github.com/ObsidianMC/Obsidian")),
                DialogAction.Create("Discord", DialogClickAction.OpenUrl("https://discord.gg/gQBtqyXChu")))
            .WithColumns(2)
            .WithExitAction(DialogAction.Create("Continue", DialogClickAction.Custom(WelcomeContinueActionId)))
            .WithPause(false)
            .AfterAction("none");
        if (feedbackEnabled)
            builder.WithMessage("Press G (the default Quick Actions key) to give feedback in-game. Your feedback helps improve Obsidian!", width: 300);
        return builder.Build();
    }

    public ValueTask ShowDialogAsync(DialogElement dialog)
    {
        dialog.Validate();
        return Client.QueuePacketAsync(new ShowDialogPacket { Dialog = dialog });
    }

    public ValueTask ClearDialogAsync() => Client.QueuePacketAsync(new ClearDialogPacket());
}
