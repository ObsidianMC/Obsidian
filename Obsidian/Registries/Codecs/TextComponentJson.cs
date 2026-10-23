using System.IO;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// Checks text components in data files as vanilla's <c>ComponentSerialization.CODEC</c> reads them, for the files
/// that keep a component Obsidian doesn't send or show, like a bed rule's message or a pack's description.
/// </summary>
/// <remarks>
/// A component is a string, a non-empty list whose first component has the rest as extras, or an object with
/// optional <c>extra</c> components and one of vanilla's contents. An object's content is the one its <c>type</c>
/// names, or without <c>type</c>, like vanilla's fuzzy matching, any content its fields make up: an object content's
/// <c>sprite</c> or <c>player</c> alone is one, as its own <c>object</c> type is matched the same way. The fields of
/// text, translations and object contents are checked like vanilla's codecs check them; the other contents only by
/// their key, and styles not at all.
/// </remarks>
internal static class TextComponentJson
{
    // Vanilla's content types, in the order its fuzzy codec tries them, with the field naming each.
    private static readonly (string Type, string Key, Action<JsonElement, string> Check)[] contents =
    [
        ("text", "text", (value, path) => String(Required(value, path, "text"), Field(path, "text"))),
        ("translatable", "translate", CheckTranslatable),
        ("keybind", "keybind", (value, path) => Required(value, path, "keybind")),
        ("score", "score", (value, path) => Required(value, path, "score")),
        ("selector", "selector", (value, path) => Required(value, path, "selector")),
        ("nbt", "nbt", (value, path) => Required(value, path, "nbt")),
        ("object", "object", CheckObject)
    ];

    // Vanilla's object infos, in the order its fuzzy codec tries them, with the field naming each.
    private static readonly (string Type, string Key, Action<JsonElement, string> Check)[] objectInfos =
    [
        ("atlas", "sprite", CheckAtlasSprite),
        ("player", "player", CheckPlayerSprite)
    ];

    /// <exception cref="InvalidDataException">The component isn't valid.</exception>
    public static void Check(JsonElement value, string path)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return;
            case JsonValueKind.Array:
                CheckList(value, path);
                return;
            case JsonValueKind.Object:
                break;
            default:
                throw Error(path, "expected a text component: a string, a list or an object");
        }

        CheckDispatched(value, path, "type", "text content", contents);

        if (TryGet(value, "extra", out var extra))
            CheckList(extra, Field(path, "extra"));
    }

    private static void CheckList(JsonElement value, string path)
    {
        if (Array(value, path).GetArrayLength() == 0)
            throw Error(path, "a list of text components can't be empty");

        var index = 0;
        foreach (var component in value.EnumerateArray())
            Check(component, Index(path, index++));
    }

    // Vanilla's legacy component matcher: the entry typeField names, or the first one whose fields are valid. When
    // none is, the error is the first entry's whose field is there, for a message naming the actual mistake.
    private static void CheckDispatched(
        JsonElement value,
        string path,
        string typeField,
        string kind,
        (string Type, string Key, Action<JsonElement, string> Check)[] entries)
    {
        if (TryGet(value, typeField, out var type))
        {
            var typePath = Field(path, typeField);
            var named = String(type, typePath);
            var entry = entries.FirstOrDefault(entry => entry.Type == named);

            if (entry.Check is null)
                throw Error(typePath, $"{named} isn't a {kind} type");

            entry.Check(value, path);
            return;
        }

        InvalidDataException? error = null;

        foreach (var entry in entries)
        {
            try
            {
                entry.Check(value, path);
                return;
            }
            catch (InvalidDataException ex)
            {
                if (error is null && value.TryGetProperty(entry.Key, out _))
                    error = ex;
            }
        }

        var keys = string.Join(", ", entries.Select(entry => entry.Key));
        throw error ?? Error(path, $"expected one of a {kind}'s fields: {keys}");
    }

    // A translation's "with" arguments are components or plain values.
    private static void CheckTranslatable(JsonElement value, string path)
    {
        String(Required(value, path, "translate"), Field(path, "translate"));

        if (TryGet(value, "fallback", out var fallback))
            String(fallback, Field(path, "fallback"));

        if (!TryGet(value, "with", out var with))
            return;

        var withPath = Field(path, "with");
        var index = 0;

        foreach (var argument in Array(with, withPath).EnumerateArray())
        {
            var argumentPath = Index(withPath, index++);
            if (argument.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                Check(argument, argumentPath);
        }
    }

    // Vanilla's ObjectInfos codec, typed by "object" or matched by its fields like contents are.
    private static void CheckObject(JsonElement value, string path) =>
        CheckDispatched(value, path, "object", "text object", objectInfos);

    // Vanilla's AtlasSprite: a sprite and an optional atlas, both identifiers.
    private static void CheckAtlasSprite(JsonElement value, string path)
    {
        Identifier(Required(value, path, "sprite"), Field(path, "sprite"));

        if (TryGet(value, "atlas", out var atlas))
            Identifier(atlas, Field(path, "atlas"));
    }

    // Vanilla's PlayerSprite: a player's profile and an optional hat flag.
    private static void CheckPlayerSprite(JsonElement value, string path)
    {
        CheckProfile(Required(value, path, "player"), Field(path, "player"));

        if (TryGet(value, "hat", out var hat))
            Bool(hat, Field(path, "hat"));
    }

    // Vanilla's ResolvableProfile: a player's name, or an object whose name and id are optional. Properties and skin
    // patches aren't checked.
    private static void CheckProfile(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            CheckPlayerName(value, path);
            return;
        }

        Object(value, path);

        if (TryGet(value, "name", out var name))
            CheckPlayerName(name, Field(path, "name"));

        if (!TryGet(value, "id", out var id))
            return;

        // UUIDUtil.CODEC: four ints.
        var idPath = Field(path, "id");
        if (Array(id, idPath).GetArrayLength() != 4)
            throw Error(idPath, "expected a UUID as a list of 4 ints");

        var index = 0;
        foreach (var part in id.EnumerateArray())
            Int(part, Index(idPath, index++));
    }

    // ExtraCodecs.PLAYER_NAME: up to 16 characters, none of them a space, control character or past '~'.
    private static void CheckPlayerName(JsonElement value, string path)
    {
        var name = String(value, path);
        if (name.Length > 16 || name.Any(c => c <= ' ' || c >= 127))
            throw Error(path, $"\"{name}\" isn't a valid player name");
    }
}
