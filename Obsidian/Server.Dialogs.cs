using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Builders;
using Obsidian.API.Registry.Codecs.Dialogs;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Text;

namespace Obsidian;

public sealed partial class Server
{
    private const string FeedbackActionId = "obsidian:feedback";
    private const int MaximumFeedbackLength = 1500;
    private const int DiscordComponentsV2 = 1 << 15;
    private const int DiscordContainer = 17;
    private const int DiscordTextDisplay = 10;
    private const int DiscordSeparator = 14;
    private const int FeedbackComponentTextBudget = 4000;
    private const int MaximumMetadataValueLength = 64;
    private static readonly TimeSpan FeedbackCooldown = TimeSpan.FromMinutes(1);
    internal static readonly HttpRequestOptionsKey<bool> FeedbackRequestOption = new("Obsidian.Feedback");
    private readonly Dictionary<Guid, DateTimeOffset> feedbackCooldowns = [];

    internal (Dictionary<string, ICodec> Codecs, Dictionary<string, Tag[]> Tags) CreateDialogConfiguration()
    {
        var codecs = CodecRegistry.Dialog.All.Values.OrderBy(x => x.Id).ToDictionary(x => x.Name, x => (ICodec)x);
        var registrations = DialogRegistry.GetRegistrations();
        if (TryGetFeedbackWebhook(Configuration.FeedbackWebhookUrl, out _))
            registrations = registrations.Add(new(FeedbackActionId, CreateFeedbackDialog(), true, false));

        var quickActions = new List<int>();
        var pauseScreen = new List<int>();
        foreach (var registration in registrations)
        {
            var id = codecs.Count;
            codecs.Add(registration.Id, new DialogCodec { Name = registration.Id, Id = id, Element = registration.Dialog });
            if (registration.QuickAction) quickActions.Add(id);
            if (registration.PauseScreen) pauseScreen.Add(id);
        }
        var tags = TagsRegistry.Categories.ToDictionary(x => x.Key, x => x.Value);
        var dialogTags = tags.GetValueOrDefault("dialog") ?? [];
        tags["dialog"] = [.. dialogTags.Where(x => x.Name is not ("quick_actions" or "pause_screen_additions")),
            MergeTag("quick_actions", quickActions), MergeTag("pause_screen_additions", pauseScreen)];
        return (codecs, tags);

        Tag MergeTag(string name, List<int> additions) => new()
        {
            Name = name, Type = "dialog",
            Entries = [.. (dialogTags.FirstOrDefault(x => x.Name == name)?.Entries ?? []).Concat(additions).Distinct()]
        };
    }

    private static DialogElement CreateFeedbackDialog() => DialogBuilder.Notice("Feedback",
            DialogAction.Create("Send", DialogClickAction.Submit(FeedbackActionId)))
        .WithMessage("Send feedback from in-game to Discord. Includes your name, UUID, world, dimension, coordinates, game mode, health, food, XP level, ping, client brand, operator status and submission time.")
        .WithInput(DialogInput.Text("message", "Your feedback", MaximumFeedbackLength, width: 300, multiline: true, height: 100))
        .WithPause(false)
        .AfterAction("wait_for_response")
        .Build();

    internal static bool TryGetFeedbackWebhook(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) || candidate.Scheme != "https" ||
            candidate.Port != 443 || candidate.UserInfo.Length != 0 ||
            candidate.Host is not ("discord.com" or "discordapp.com" or "canary.discord.com" or "ptb.discord.com"))
            return false;
        var parts = candidate.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var webhookIndex = parts.Length == 4 ? 1 : 2;
        if (parts.Length is not (4 or 5) || parts[0] != "api" || parts[webhookIndex] != "webhooks" ||
            (parts.Length == 5 && (parts[1].Length < 2 || parts[1][0] != 'v' || !parts[1][1..].All(char.IsAsciiDigit))) ||
            !parts[webhookIndex + 1].All(char.IsAsciiDigit) || parts[webhookIndex + 2].Length == 0)
            return false;
        uri = candidate;
        return true;
    }

    internal async ValueTask HandleFeedbackAsync(Player player, string actionId, INbtTag? payload)
    {
        if (actionId != FeedbackActionId) return;
        if (!TryGetFeedbackWebhook(Configuration.FeedbackWebhookUrl, out var webhook))
        {
            await Respond("Feedback is currently unavailable.");
            return;
        }
        if (payload is not NbtCompound compound || !compound.TryGetTagValue<string>("message", out var message) ||
            string.IsNullOrWhiteSpace(message) || message.Length > MaximumFeedbackLength)
        {
            await Respond($"Please enter feedback between 1 and {MaximumFeedbackLength} characters.");
            return;
        }
        var now = DateTimeOffset.UtcNow;
        bool throttled;
        lock (feedbackCooldowns)
        {
            foreach (var expired in feedbackCooldowns.Where(x => x.Value <= now).Select(x => x.Key).ToArray())
                feedbackCooldowns.Remove(expired);
            throttled = feedbackCooldowns.ContainsKey(player.Uuid);
            if (!throttled) feedbackCooldowns[player.Uuid] = now + FeedbackCooldown;
        }
        if (throttled)
        {
            await Respond("Please wait a minute between feedback submissions.");
            return;
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancelTokenSource.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var http = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Feedback");
            using var request = new HttpRequestMessage(HttpMethod.Post, GetFeedbackComponentWebhook(webhook!))
            {
                Content = CreateFeedbackContent(message.Trim(), FormatFeedbackPlayer(player, Operators.IsOperator(player), now))
            };
            request.Options.Set(FeedbackRequestOption, true);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.IsSuccessStatusCode) await Respond("Thank you! Your feedback was sent.");
            else
            {
                logger.LogWarning("Feedback webhook returned HTTP {StatusCode}", (int)response.StatusCode);
                await Respond("Feedback could not be sent. Please try again later.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Exception messages may contain the secret webhook URL.
            logger.LogWarning("Feedback delivery failed ({ErrorType})", ex.GetType().Name);
            await Respond("Feedback could not be sent. Please try again later.");
        }

        ValueTask Respond(string text) => player.ShowDialogAsync(DialogBuilder.Notice("Feedback").WithMessage(text).Build());
    }

    internal static Uri GetFeedbackComponentWebhook(Uri webhook)
    {
        var query = webhook.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter =>
            {
                var name = Uri.UnescapeDataString(parameter.Split('=', 2)[0]);
                return !name.Equals("with_components", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("wait", StringComparison.OrdinalIgnoreCase);
            });
        return new UriBuilder(webhook) { Query = string.Join('&', query.Append("with_components=true").Append("wait=true")) }.Uri;
    }

    internal static JsonContent CreateFeedbackContent(string message, string playerDetails)
    {
        const string title = "## Player feedback";
        var feedback = $"### Feedback\n{EscapeDiscordMarkdown(message)}";
        var remaining = FeedbackComponentTextBudget - title.Length - feedback.Length;
        if (remaining < 1) throw new ArgumentException("Feedback exceeds the component text budget.", nameof(message));
        if (playerDetails.Length > remaining) playerDetails = playerDetails[..(remaining - 1)] + "…";

        return JsonContent.Create(new
        {
            flags = DiscordComponentsV2,
            allowed_mentions = new { parse = Array.Empty<string>() },
            components = new[]
            {
                new
                {
                    type = DiscordContainer,
                    components = new object[]
                    {
                        new { type = DiscordTextDisplay, content = title },
                        new { type = DiscordTextDisplay, content = feedback },
                        new { type = DiscordSeparator, divider = true, spacing = 1 },
                        new { type = DiscordTextDisplay, content = playerDetails }
                    }
                }
            }
        });
    }

    private static string FormatFeedbackPlayer(IPlayer player, bool isOperator, DateTimeOffset submittedAt)
    {
        var level = player.Level;
        var world = level is IDimension dimension ? dimension.ParentWorld.Name : level.Name;
        var position = player.Position;
        return FormattableString.Invariant($"""
            ### Reporting player
            **Username:** {MetadataValue(player.Username)} · **Operator:** {(isOperator ? "Yes" : "No")}
            **UUID:** {player.Uuid}

            ### Location
            **World:** {MetadataValue(world)}
            **Dimension:** {MetadataValue(level.DimensionName)}
            **Position:** {position.X:0.##}, {position.Y:0.##}, {position.Z:0.##}

            ### Gameplay
            **Game mode:** {player.GameMode} · **XP level:** {player.XpLevel}
            **Health:** {player.Health:0.##} · **Food:** {player.FoodLevel}

            ### Connection
            **Client:** {MetadataValue(player.Client.Brand ?? "Unknown")} · **Ping:** {player.Ping} ms

            -# Submitted: {submittedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC
            """);
    }

    private static string MetadataValue(string value)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ');
        if (value.Length > MaximumMetadataValueLength) value = value[..MaximumMetadataValueLength] + "…";
        return EscapeDiscordMarkdown(value);
    }

    private static string EscapeDiscordMarkdown(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if ("\\`*_~|>#[]()-+!".Contains(character)) result.Append('\\');
            result.Append(character);
        }
        return result.ToString();
    }
}
