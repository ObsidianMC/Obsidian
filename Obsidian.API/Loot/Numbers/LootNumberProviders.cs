using Obsidian.API.Data;
using System.Text.Json;

namespace Obsidian.API.Loot.Numbers;

/// <summary>Adapts the shared typed context providers to the existing plugin loot API.</summary>
public static class LootNumberProviders
{
    /// <summary>
    /// Parses an integer number provider from vanilla's JSON form (a number, a provider id or an object with a
    /// <c>type</c>).
    /// </summary>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="json"/> isn't valid JSON.</exception>
    /// <exception cref="System.IO.InvalidDataException">The provider type isn't supported.</exception>
    public static INumberProvider ParseInt(string json) => new IntNumber(ContextNumberProviders.ParseInt(json));

    /// <summary>
    /// Parses a float number provider from vanilla's JSON form (a number, a provider id or an object with a
    /// <c>type</c>).
    /// </summary>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="json"/> isn't valid JSON.</exception>
    /// <exception cref="System.IO.InvalidDataException">The provider type isn't supported.</exception>
    public static INumberProvider ParseFloat(string json) => new FloatNumber(ContextNumberProviders.ParseFloat(json));

    /// <summary>
    /// Reads an integer number provider from an already parsed JSON value (see <see cref="ParseInt"/>).
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">The provider type isn't supported.</exception>
    public static INumberProvider ReadInt(JsonElement json) => new IntNumber(ContextNumberProviders.ReadInt(json));

    /// <summary>
    /// Reads a float number provider from an already parsed JSON value (see <see cref="ParseFloat"/>).
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">The provider type isn't supported.</exception>
    public static INumberProvider ReadFloat(JsonElement json) =>
        new FloatNumber(ContextNumberProviders.ReadFloat(json));

    private sealed class IntNumber(ContextIntProvider provider) : INumberProvider
    {
        public int GetInt(LootContext context) => provider.GetInt(context.Numbers);

        public float GetFloat(LootContext context) => provider.GetInt(context.Numbers);
    }

    private sealed class FloatNumber(ContextFloatProvider provider) : INumberProvider
    {
        public float GetFloat(LootContext context) => provider.GetFloat(context.Numbers);
    }
}
