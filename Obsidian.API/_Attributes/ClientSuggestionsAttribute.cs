namespace Obsidian.API;

/// <summary>
/// Has the client suggest values for a command parameter with one of its own suggestion providers, instead of asking
/// the server.
/// </summary>
/// <param name="provider">
/// The provider's identifier: <c>minecraft:available_sounds</c> or <c>minecraft:summonable_entities</c>.
/// </param>
/// <example><c>public Task PlaySound([ClientSuggestions(ClientSuggestionsAttribute.AvailableSounds)] ResourceLocation sound)</c></example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class ClientSuggestionsAttribute(string provider) : Attribute
{
    /// <summary>
    /// The client's provider of every sound it knows.
    /// </summary>
    public const string AvailableSounds = "minecraft:available_sounds";
    /// <summary>
    /// The client's provider of the entity types that can be summoned.
    /// </summary>
    public const string SummonableEntities = "minecraft:summonable_entities";

    /// <summary>
    /// Gets the identifier of the client's suggestion provider.
    /// </summary>
    public string Provider { get; } = provider;
}
