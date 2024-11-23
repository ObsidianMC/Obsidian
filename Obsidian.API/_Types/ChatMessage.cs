using System.Text.Json;

namespace Obsidian.API;

public sealed class ChatMessage : INetworkSerializable<ChatMessage>
{
    public string? Text { get; set; }

    public string? Translate { get; set; }

    public HexColor? Color { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public bool Underlined { get; set; }

    public bool Strikethrough { get; set; }

    public bool Obfuscated { get; set; }

    public string? Insertion { get; set; }

    public ClickComponent? ClickEvent { get; set; }

    public HoverComponent? HoverEvent { get; set; }

    public List<ChatMessage>? Extra { get; private set; }

    public List<ChatMessage>? With { get; private set; }

    public IEnumerable<ChatMessage> GetExtraChatComponents() => With ?? Enumerable.Empty<ChatMessage>();

    public IEnumerable<ChatMessage> GetExtras() => Extra ?? Enumerable.Empty<ChatMessage>();

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
    /// <param name="translate">The translate value.</param>
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
    /// <param name="translate">The translate value.</param>
    /// <returns>The given <see cref="ChatMessage"/>.</returns>
    public static ChatMessage Hover(ChatMessage message, HoverAction action, IHoverContent contents)
    {
        message.HoverEvent = new HoverComponent { Action = action, Contents = contents };
        return message;
    }

    /// <summary>
    /// Converts all formatting codes which are using '&' to their respective '§' formatting code.
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

    public ChatMessage AddChatComponent(ChatMessage message)
    {
        this.With ??= [];
        this.With.Add(message);

        return this;
    }

    public ChatMessage AddChatComponent(IEnumerable<ChatMessage> message)
    {
        this.With ??= [];
        this.With.AddRange(message);

        return this;
    }


    public ChatMessage AddExtra(ChatMessage message)
    {
        Extra ??= [];
        Extra.Add(message);

        return this;
    }

    public ChatMessage AddExtra(List<ChatMessage> messages)
    {
        Extra ??= new List<ChatMessage>(capacity: messages.Count);
        Extra.AddRange(messages);

        return this;
    }

    public ChatMessage AddExtra(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            AddExtra(message);
        }

        return this;
    }

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

    public static ChatMessage Empty => Simple(string.Empty);

    public string ToString(JsonSerializerOptions options) => JsonSerializer.Serialize(this, options);
    public static void Write(ChatMessage value, INetStreamWriter writer) => writer.WriteChat(value);
}
