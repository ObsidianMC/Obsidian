using System.Text.Json;

namespace Obsidian.API;

/// <summary>
/// A chat text component: text or a translation key with styling, click and hover actions, and child components.
/// </summary>
public sealed record class ChatMessage : INetworkSerializable<ChatMessage>
{
    /// <summary>Gets or sets the literal text. It may hold legacy <c>§</c> formatting codes.</summary>
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets a translation key the client looks up in its language file, such as <c>chat.type.text</c>;
    /// <see cref="With"/> fills its placeholders.
    /// </summary>
    public string? Translate { get; set; }

    /// <summary>The key bound to this keybind, like <c>key.jump</c>, as the client shows it.</summary>
    public string? Keybind { get; set; }

    /// <summary>Gets or sets the text color, or null to inherit it.</summary>
    public HexColor? Color { get; set; }

    // The style flags are inherited from the parent component (or the context, like italic custom item names) when null.

    /// <summary>Gets or sets whether the text is bold, or null to inherit it.</summary>
    public bool? Bold { get; set; }

    /// <summary>Gets or sets whether the text is italic, or null to inherit it.</summary>
    public bool? Italic { get; set; }

    /// <summary>Gets or sets whether the text is underlined, or null to inherit it.</summary>
    public bool? Underlined { get; set; }

    /// <summary>Gets or sets whether the text is struck through, or null to inherit it.</summary>
    public bool? Strikethrough { get; set; }

    /// <summary>Gets or sets whether the text is scrambled, or null to inherit it.</summary>
    public bool? Obfuscated { get; set; }

    /// <summary>Optional signed ARGB shadow color from the text component codec.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("shadow_color")]
    public int? ShadowColor { get; set; }

    /// <summary>Gets or sets text inserted into the player's chat box when they shift-click this component.</summary>
    public string? Insertion { get; set; }

    /// <summary>Gets or sets what happens when a player clicks this component.</summary>
    public ClickComponent? ClickEvent { get; set; }

    /// <summary>Gets or sets what is shown when a player hovers over this component.</summary>
    public HoverComponent? HoverEvent { get; set; }

    /// <summary>
    /// Gets the child components shown after this one, which inherit its style; null when there are none.
    /// </summary>
    public List<ChatMessage>? Extra { get; private set; }

    /// <summary>
    /// Gets the values that fill the <see cref="Translate"/> key's placeholders; null when there are none.
    /// </summary>
    public List<ChatMessage>? With { get; private set; }

    /// <summary>
    /// Gets the values that fill the <see cref="Translate"/> key's placeholders, or an empty sequence.
    /// </summary>
    public IEnumerable<ChatMessage> GetExtraChatComponents() => With ?? Enumerable.Empty<ChatMessage>();

    /// <summary>Gets the child components, or an empty sequence.</summary>
    public IEnumerable<ChatMessage> GetExtras() => Extra ?? Enumerable.Empty<ChatMessage>();

    /// <summary>Creates a plain text component, as <see cref="Simple(string)"/> does.</summary>
    public static implicit operator ChatMessage(string text) => Simple(text);

    /// <summary>
    /// Adds the right <see cref="ChatMessage"/> to the <see cref="Extra"/> of the left <see cref="ChatMessage"/>.
    /// </summary>
    /// <param name="a">The left chat message on which the right one gets appended.</param>
    /// <param name="b">The right chat message which will be appended.</param>
    /// <returns>The modified chat message.</returns>
    public static ChatMessage operator +(ChatMessage a, ChatMessage b) => a.AddExtra(b);

    /// <summary>
    /// Adds the given <see cref="ChatColor"/> to the text of the given <see cref="ChatMessage"/>.
    /// </summary>
    /// <param name="a">The message on which the chat color gets appended.</param>
    /// <param name="b">The chat color which will be appended.</param>
    /// <returns>The modified chat message.</returns>
    public static ChatMessage operator +(ChatMessage a, ChatColor b) => a.AppendColor(b);

    /// <summary>
    /// Creates a new <see cref="ChatMessage"/> object with plain text.
    /// </summary>
    /// <param name="text">The text of the <see cref="ChatMessage"/>.</param>
    /// <returns>The created <see cref="ChatMessage"/> object.</returns>
    public static ChatMessage Simple(string text) => new() { Text = text };

    /// <summary>
    /// Creates a new <see cref="ChatMessage"/> object with plain text. The text will be reformatted by using
    /// the <see cref="ReformatAmpersandPrefixes"/> method.
    /// </summary>
    /// <param name="text">The text of the <see cref="ChatMessage"/>.</param>
    /// <returns>The created <see cref="ChatMessage"/> object.</returns>
    public static ChatMessage SimpleLegacy(string text) => new() { Text = ReformatAmpersandPrefixes(text) };

    /// <summary>
    /// Creates a new <see cref="ChatMessage"/> object with plain text.
    /// </summary>
    /// <param name="text">The text of the <see cref="ChatMessage"/>.</param>
    /// <param name="color">The <see cref="ChatColor"/> of the <see cref="ChatMessage"/>.</param>
    /// <returns>The created <see cref="ChatMessage"/> object.</returns>
    public static ChatMessage Simple(string text, ChatColor color) => new()
    {
        Text = $"{color}{text}"
    };

    /// <summary>
    /// Creates a component that the client shows as a player's chat line, <c>&lt;username&gt; text</c>.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="username">The sender's name; null is shown as empty.</param>
    public static ChatMessage TranslatableChatMessageType(string text, string username)
    {
        var message = new ChatMessage()
        {
            Translate = "chat.type.text",
        }
        .AddChatComponent(username ?? string.Empty)
        .AddChatComponent(text);

        return message;
    }

    /// <summary>
    /// Creates a component that the client shows as a player's chat line, <c>&lt;username&gt; text</c>, with the
    /// message in a color.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="color">The color code put before the message.</param>
    /// <param name="username">The sender's name; null is shown as empty.</param>
    public static ChatMessage TranslatableChatMessageType(string text, ChatColor color, string username)
    {
        var message = new ChatMessage()
        {
            Translate = "chat.type.text",
        }
        .AddChatComponent(username ?? string.Empty)
        .AddChatComponent($"{color}{text}");

        return message;
    }

    /// <summary>
    /// Creates a new <see cref="ChatMessage"/> object with plain text. The text will be reformatted by using
    /// the <see cref="ReformatAmpersandPrefixes"/> method.
    /// </summary>
    /// <param name="text">The text of the <see cref="ChatMessage"/>.</param>
    /// <param name="color">The <see cref="ChatColor"/> of the <see cref="ChatMessage"/>.</param>
    /// <returns>The created <see cref="ChatMessage"/> object.</returns>
    public static ChatMessage SimpleLegacy(string text, ChatColor color) => new()
    {
        Text = $"{color}{ReformatAmpersandPrefixes(text)}"
    };

    /// <summary>
    /// Appends an underlying <see cref="ClickEvent"/> (<see cref="ClickComponent"/>) to the given <see cref="ChatMessage"/>.
    /// </summary>
    /// <param name="message">The message which will hold the <see cref="ClickComponent"/>.</param>
    /// <param name="action">The action which will be executed when clicking.</param>
    /// <param name="value">The value which will be executed with the action.</param>
    /// <returns>The given <see cref="ChatMessage"/>.</returns>
    public static ChatMessage Click(ChatMessage message, ClickAction action, string value)
    {
        message.ClickEvent = new ClickComponent { Action = action, Value = value };
        return message;
    }

    /// <summary>
    /// Appends an underlying <see cref="HoverEvent"/> (<see cref="HoverComponent"/>) to the given <see cref="ChatMessage"/>.
    /// </summary>
    /// <param name="message">The message which will hold the <see cref="HoverComponent"/>.</param>
    /// <param name="action">The action which will be executed when clicking.</param>
    /// <param name="contents">The contents which will be executed with the action.</param>
    /// <returns>The given <see cref="ChatMessage"/>.</returns>
    public static ChatMessage Hover(ChatMessage message, HoverAction action, IHoverContent contents)
    {
        message.HoverEvent = new HoverComponent { Action = action, Contents = contents };
        return message;
    }

    /// <summary>
    /// Replaces <c>&amp;</c> with <c>§</c> where it comes before one of the lowercase codes <c>0</c>-<c>9</c>,
    /// <c>a</c>-<c>e</c>, <c>k</c>-<c>o</c> or <c>r</c>. <c>&amp;f</c> and uppercase codes are left as they are.
    /// </summary>
    /// <param name="originalText">The text to be reformatted.</param>
    /// <returns>The formatted text.</returns>
    public static string ReformatAmpersandPrefixes(string originalText)
    {
        return string.Create(originalText.Length, originalText, (span, text) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                char c = text[i];
                span[i] = c;

                if (c == '&' && i + 1 < text.Length)
                {
                    c = text[i + 1];
                    if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'e') || (c >= 'k' && c <= 'o') || c == 'r')
                    {
                        span[i] = '§';
                    }
                }
            }
        });
    }

    /// <summary>Adds a value for the <see cref="Translate"/> key's next placeholder.</summary>
    /// <returns>This component.</returns>
    public ChatMessage AddChatComponent(ChatMessage message)
    {
        this.With ??= [];
        this.With.Add(message);

        return this;
    }

    /// <summary>Adds values for the <see cref="Translate"/> key's next placeholders.</summary>
    /// <returns>This component.</returns>
    public ChatMessage AddChatComponent(IEnumerable<ChatMessage> message)
    {
        this.With ??= [];
        this.With.AddRange(message);

        return this;
    }


    /// <summary>Adds a child component after this one's existing children.</summary>
    /// <returns>This component.</returns>
    public ChatMessage AddExtra(ChatMessage message)
    {
        Extra ??= [];
        Extra.Add(message);

        return this;
    }

    /// <summary>Adds child components after this one's existing children.</summary>
    /// <returns>This component.</returns>
    public ChatMessage AddExtra(List<ChatMessage> messages)
    {
        Extra ??= new List<ChatMessage>(capacity: messages.Count);
        Extra.AddRange(messages);

        return this;
    }

    /// <summary>Adds child components after this one's existing children.</summary>
    /// <returns>This component.</returns>
    public ChatMessage AddExtra(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            AddExtra(message);
        }

        return this;
    }

    /// <summary>Appends text to <see cref="Text"/>.</summary>
    /// <returns>This component.</returns>
    public ChatMessage AppendText(string text)
    {
        if (Text is null)
        {
            Text = text;
        }
        else
        {
            Text += text;
        }
        return this;
    }

    /// <summary>Appends a legacy <c>§</c> formatting code to <see cref="Text"/>.</summary>
    /// <returns>This component.</returns>
    public ChatMessage AppendColor(ChatColor color)
    {
        if (Text is null)
        {
            Text = color.ToString();
        }
        else
        {
            Text += color.ToString();
        }

        return this;
    }

    /// <summary>Appends text, preceded by a legacy <c>§</c> formatting code, to <see cref="Text"/>.</summary>
    /// <returns>This component.</returns>
    public ChatMessage AppendText(string text, ChatColor color)
    {
        if (Text is null)
        {
            Text = $"{color}{text}";
        }
        else
        {
            Text += $"{color}{text}";
        }
        return this;
    }

    // Component values must not retain mutable chat objects supplied by their callers.
    internal ChatMessage CopyForComponent() => this with
    {
        ClickEvent = this.ClickEvent is null ? null : new ClickComponent
        {
            Action = this.ClickEvent.Action,
            Value = this.ClickEvent.Value
        },
        HoverEvent = this.HoverEvent is null ? null : this.HoverEvent with
        {
            Contents = this.HoverEvent.Contents switch
            {
                HoverChatContent chat => chat with { ChatMessage = chat.ChatMessage.CopyForComponent() },
                HoverItemContent item => item with { Item = new(item.Item, item.Item.Count) },
                _ => throw new NotSupportedException("This hover content cannot be stored in an item component.")
            }
        },
        Extra = this.Extra?.Select(message => message.CopyForComponent()).ToList(),
        With = this.With?.Select(message => message.CopyForComponent()).ToList()
    };

    /// <summary>Gets a new component with empty text.</summary>
    public static ChatMessage Empty => Simple(string.Empty);

    /// <summary>Serializes the component to JSON with the given options.</summary>
    public string ToString(JsonSerializerOptions options) => JsonSerializer.Serialize(this, options);
    /// <summary>Writes a component to a network stream.</summary>
    public static void Write(ChatMessage value, INetStreamWriter writer) => writer.WriteChat(value);
    /// <summary>Reads a component from a network stream.</summary>
    public static ChatMessage Read(INetStreamReader reader) => reader.ReadChat();
}
