using Obsidian.API.Crafting;
using Microsoft.Extensions.Logging;
using Obsidian.Plugins;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// A server's recipe registrations. Only vanilla recipes and lightweight book identities are shared across servers.
/// </summary>
internal sealed class ServerRecipeRegistry
{
    private readonly Func<ContentVersion> contentVersion;
    private readonly Lock gate = new();
    private readonly Dictionary<string, IRecipe> registered = new(RecipesRegistry.Current.Recipes);
    private readonly Dictionary<string, long> revisions = [];
    private long revision;
    private volatile RecipeSet current = RecipesRegistry.Current;
    private List<(PluginContainer Owner, IRecipe Recipe)> fileRecipes = [];
    private PluginRecipeValue[] customRecipes = [];
    private readonly Dictionary<string, long> fileRevisions = [];

    internal ServerRecipeRegistry(Func<ContentVersion> contentVersion)
    {
        this.contentVersion = contentVersion;
        this.Types = new PluginRecipeTypesRegistry(this.RemoveRecipeTypes);
    }

    internal PluginRecipeTypesRegistry Types { get; }

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
                    if (this.fileRecipes.Any(file => file.Recipe.Identifier == key))
                        throw new ArgumentException($"Recipe {key} is already registered.");

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

    /// <summary>Rebuilds file recipes after tags or plugins change, with serializers outside the lock.</summary>
    internal void ReplaceFiles(
        IEnumerable<(PluginContainer Owner, IEnumerable<RecipeFile> Files)> sources, ILogger logger)
    {
        var vanilla = new List<(PluginContainer Owner, IRecipe Recipe)>();
        var custom = new Dictionary<string, PluginRecipeValue>();
        foreach (var (owner, files) in sources)
        {
            if (owner.IsClosing)
                continue;

            foreach (var file in files)
            {
                try
                {
                    var type = file.Json.GetProperty("type").GetString()!;
                    if (this.Types.Get(type) is RecipeTypeBinding binding)
                    {
                        using var call = binding.Owner.TryEnter();
                        if (call is not null && binding.Active)
                        {
                            var value = binding.Read(file.Identifier, file.Json)
                                ?? throw new InvalidOperationException("A recipe serializer returned null.");
                            custom.Add(file.Identifier, new PluginRecipeValue(owner, file.Identifier, binding, value));
                        }
                    }
                    else
                    {
                        if (type.Contains(':') && !type.StartsWith("minecraft:", StringComparison.Ordinal))
                            throw new System.Text.Json.JsonException($"Unknown recipe type {type}.");

                        foreach (var recipe in PluginRecipeFiles.ReadVanilla(file))
                            vanilla.Add((owner, RecipesRegistry.Snapshot(recipe)));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not deserialize recipe {Recipe} from {Plugin}",
                        file.Identifier, owner.Info.Id);
                }
            }
        }

        lock (this.gate)
        {
            this.fileRecipes = vanilla;
            this.customRecipes = custom.Values
                .Where(recipe => recipe.Binding.Active && !recipe.Owner.IsClosing && !recipe.Binding.Owner.IsClosing)
                .ToArray();
            this.fileRevisions.Clear();
            foreach (var (_, recipe) in vanilla)
                this.fileRevisions[recipe.Identifier] = ++this.revision;

            this.Publish();
        }
    }

    private void RemoveRecipeTypes(IReadOnlySet<string> removed)
    {
        lock (this.gate)
        {
            this.customRecipes = this.customRecipes.Where(recipe => !removed.Contains(recipe.Binding.Identifier))
                .ToArray();
            this.Publish();
        }
    }

    private void Publish()
    {
        var recipes = new Dictionary<string, IRecipe>(this.registered);
        var versions = new Dictionary<string, long>(this.revisions);
        foreach (var (owner, recipe) in this.fileRecipes)
        {
            if (owner.IsClosing || recipes.ContainsKey(recipe.Identifier))
                continue;

            recipes.Add(recipe.Identifier, recipe);
            versions[recipe.Identifier] = this.fileRevisions[recipe.Identifier];
        }

        this.current = RecipesRegistry.BuildSet(recipes, versions, this.contentVersion(), this.customRecipes);
    }
}
