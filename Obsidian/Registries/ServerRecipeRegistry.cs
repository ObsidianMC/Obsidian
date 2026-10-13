using Obsidian.API.Crafting;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// A server's recipe registrations. Only vanilla recipes and lightweight book identities are shared across servers.
/// </summary>
internal sealed class ServerRecipeRegistry(Func<ContentVersion> contentVersion)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, IRecipe> registered = new(RecipesRegistry.Current.Recipes);
    private readonly Dictionary<string, long> revisions = [];
    private long revision;
    private volatile RecipeSet current = RecipesRegistry.Current;

    public RecipeSet Current => this.current;

    // The returned copies are also the removal tokens, so an old owner can't remove a replacement.
    public void Register(IEnumerable<IRecipe> recipes, List<IRecipe> added)
    {
        lock (this.gate)
        {
            try
            {
                foreach (var recipe in recipes)
                {
                    var copy = RecipesRegistry.Snapshot(recipe);
                    var key = copy.Identifier.ToSnakeCase();
                    this.registered.Add(key, copy);
                    this.revisions[key] = ++this.revision;
                    added.Add(copy);
                }
            }
            finally
            {
                this.Publish();
            }
        }
    }

    public void Unregister(IEnumerable<IRecipe> recipes)
    {
        lock (this.gate)
        {
            foreach (var recipe in recipes)
            {
                var key = recipe.Identifier.ToSnakeCase();

                if (this.registered.TryGetValue(key, out var existing) && ReferenceEquals(existing, recipe))
                {
                    this.registered.Remove(key);
                    this.revisions.Remove(key);
                }
            }

            this.Publish();
        }
    }

    private void Publish() =>
        this.current = RecipesRegistry.BuildSet(this.registered, this.revisions, contentVersion());
}
