# Dialogs and quick actions

The codecs follow [Mojang's dialog format](https://www.minecraft.net/en-us/article/minecraft-java-edition-1-21-6).

Use `Obsidian.API.Builders` and `Obsidian.API.Registry.Codecs.Dialogs`.

```csharp
var dialog = DialogBuilder.Notice("Welcome")
    .WithMessage("Welcome to the server!")
    .Build();
await player.ShowDialogAsync(dialog);
await player.ClearDialogAsync();

DialogBuilder.MultiAction("Quick actions")
    .WithActions(
        DialogAction.Create("Spawn", DialogClickAction.RunCommand("spawn")),
        DialogAction.Create("Website", DialogClickAction.OpenUrl("https://example.com")))
    .WithExitAction(DialogAction.Create("Back"))
    .Register("myplugin:quick_actions", quickAction: true);
```

Register during plugin initialization, before players connect. Registration and
unregistration apply on the player's next connection. `pauseScreen: true` also
adds an entry to the pause menu. Minecraft chooses the quick-action key; G is its
default. One entry opens directly; multiple entries open the vanilla chooser.

Builders cover notice, confirmation, multi-action, server-links and dialog-list
screens. Use `DialogTag` for lists backed by a tag. Bodies support messages and
items; inputs support text (including multiline), booleans, single options and
number ranges. `WithExternalTitle`, `WithColumns`, `WithButtonWidth`, `WithEscape`,
`WithPause` and `AfterAction` configure the vanilla layout and behavior.

```csharp
var form = DialogBuilder.Notice("Suggestion",
        DialogAction.Create("Submit", DialogClickAction.Submit("myplugin:suggestion")))
    .WithInput(DialogInput.Text("message", "Suggestion", maxLength: 500))
    .Build();
```

Handle `DialogActionEventArgs` through the usual `MinecraftEventHandler` and
`[EventPriority]` mechanism. `ActionId` identifies the action; `Payload` contains
the input values for dynamic submissions, normally an `NbtCompound`. Client
actions are untrusted: check identifiers, authorization, field types and limits
in your handler. Call `SetHandled()` to prevent further handling. Buttons can
also run/suggest commands, open URLs, copy text, change book pages, show another
dialog, send static custom payloads or expand a command template.

Custom payloads are limited to 64 KiB, 32 levels of nesting and 4,096 tags.

## Feedback

First-time players also receive a welcome dialog once their client finishes
loading the world. An obsidian block appears above a bold welcome heading with
the Obsidian name in purple. It explains that Obsidian is early in development
and links to GitHub and Discord. Opening either link keeps the dialog open;
Continue closes it. When feedback is configured, it also explains the G
quick-action key. A player is considered new when neither their global
persistent file nor their current world's player file exists. Normal player
saves preserve this across reconnects and server restarts; deleting the saved
data or disconnecting before a save can show the welcome dialog again.

Set `feedbackWebhookUrl` in `config/server.json` to a Discord webhook URL:

```json
"feedbackWebhookUrl": "https://discord.com/api/webhooks/WEBHOOK_ID/WEBHOOK_TOKEN"
```

Null, empty or invalid URLs disable feedback. The form appears under quick
actions for newly connected players. Discord receives a Components V2 container
with separate feedback and reporting-player sections. Metadata includes username,
UUID, world, dimension, coordinates, game mode, health, food, XP level, ping,
client brand, operator status and the UTC submission time.
The metadata is grouped into player, location, gameplay and connection sections,
with short fields paired on each line and the submission time in a footer.
Mentions are suppressed and user text is escaped for Markdown. Messages are limited to 1,500 characters and
one submission per player UUID per minute, including reconnects. Delivery has a
10-second timeout; players receive a success or failure dialog. Staff should keep
the webhook URL private. No feedback is persisted or automatically retried.

The server adds `with_components=true` and `wait=true` to the webhook request,
preserving parameters such as `thread_id`. The latter confirms that Discord saved
the message before reporting success. Metadata text values are capped at 64
characters and the complete component text uses a 4,000-character budget.

The server reloads webhook configuration; adding/removing the quick-action entry
requires reconnecting. Existing players' submissions use the current webhook.
