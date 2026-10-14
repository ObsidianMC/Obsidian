using Obsidian.API.Inventory;
using System.Text.Json;

namespace Obsidian.API.Crafting;

/// <summary>A server-side machine recipe type, registered during a plugin's ConfigureRegistry.</summary>
/// <typeparam name="TRecipe">An immutable recipe value made by the serializer.</typeparam>
public sealed class RecipeType<TRecipe> where TRecipe : class
{
    internal RecipeType(string identifier, int id)
    {
        this.Identifier = identifier;
        this.Id = id;
    }

    /// <summary>The identifier JSON names in its type field.</summary>
    public string Identifier { get; }

    /// <summary>A process-stable identity; it is not a vanilla network registry id.</summary>
    public int Id { get; }
}

/// <summary>Reads and matches a plugin's machine recipes. Implementations and their values go on unload.</summary>
public interface IRecipeSerializer<TRecipe> where TRecipe : class
{
    /// <summary>Reads one recipe file. Return an immutable value owned by the recipe set.</summary>
    /// <param name="identifier">The recipe identifier derived from its file path.</param>
    /// <param name="json">The complete recipe object, including type. Retained elements must be cloned.</param>
    TRecipe Read(string identifier, JsonElement json);

    /// <summary>Tests inputs without changing them or the recipe; null slots are empty.</summary>
    bool Matches(TRecipe recipe, IReadOnlyList<ItemStack?> inputs);
}
