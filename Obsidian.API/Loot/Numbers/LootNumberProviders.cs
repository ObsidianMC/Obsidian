using Obsidian.API.Data;
using System.Text.Json;

namespace Obsidian.API.Loot.Numbers;

/// <summary>Adapts the shared typed context providers to the existing plugin loot API.</summary>
public static class LootNumberProviders
{
    public static INumberProvider ParseInt(string json) => new IntNumber(ContextNumberProviders.ParseInt(json));

    public static INumberProvider ParseFloat(string json) => new FloatNumber(ContextNumberProviders.ParseFloat(json));

    public static INumberProvider ReadInt(JsonElement json) => new IntNumber(ContextNumberProviders.ReadInt(json));

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
