using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.API;

public class ChatMessage
{
    public string Text { get; set; }

    public HexColor Color { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public bool Underlined { get; set; }

    public bool Strikethrough { get; set; }

    public bool Obfuscated { get; set; }

    public string Insertion { get; set; }

    public ClickComponent ClickEvent { get; set; }

    public HoverComponent HoverEvent { get; set; }

    public List<ChatMessage> Extra { get; private set; }

    [JsonIgnore]
    public IEnumerable<ChatMessage> Extras => GetExtras();

    public IEnumerable<ChatMessage> GetExtras()
    {
        if (Extra == null)
            yield break;

        foreach (var extra in Extra)
        {
            yield return extra;
        }
    }

    /// <summary>
    /// Creates a new <see cref="ChatMessage"/> object with plain text.
    /// </summary>
    public static ChatMessage Simple(string text) => new() { Text = text.Replace('&', '§') };
    public static ChatMessage Simple(string text, ChatColor color) => new() { Text = $"{color}{text}" };

    public ChatMessage AddExtra(ChatMessage message)
    {
        Extra ??= new List<ChatMessage>();
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

    public static implicit operator ChatMessage(string text) => Simple(text);

    public string ToString(JsonSerializerOptions options) => JsonSerializer.Serialize(this, options);
}
