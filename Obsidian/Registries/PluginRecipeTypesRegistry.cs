using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.Plugins;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>Server-owned serializers; shared identities contain only strings and integers.</summary>
internal sealed class PluginRecipeTypesRegistry(Action<IReadOnlySet<string>> removed)
{
    private static readonly Lock identitiesGate = new();
    private static readonly Dictionary<string, int> identities = [];
    private readonly Lock gate = new();
    private readonly Dictionary<string, RecipeTypeBinding> bindings = [];

    internal RecipeType<TRecipe> Register<TRecipe>(
        PluginContainer owner, string identifier, IRecipeSerializer<TRecipe> serializer) where TRecipe : class
    {
        ArgumentNullException.ThrowIfNull(serializer);
        PluginRegistry.CheckIdentifier(owner, identifier);
        int id;
        lock (identitiesGate)
        {
            if (!identities.TryGetValue(identifier, out id))
                identities.Add(identifier, id = identities.Count);
        }

        lock (this.gate)
        {
            owner.ThrowIfClosing();
            this.bindings.Add(identifier, new RecipeTypeBinding(
                owner,
                identifier,
                (name, json) => serializer.Read(name, json),
                (recipe, inputs) => recipe is TRecipe value && serializer.Matches(value, inputs)));
        }

        owner.TrackAddition(() => this.ReleasePlugin(owner));
        return new RecipeType<TRecipe>(identifier, id);
    }

    internal RecipeTypeBinding? Get(string identifier)
    {
        lock (this.gate)
            return this.bindings.GetValueOrDefault(identifier);
    }

    internal void ReleasePlugin(PluginContainer owner)
    {
        HashSet<string> released = [];
        lock (this.gate)
        {
            foreach (var (name, binding) in this.bindings.ToArray())
            {
                if (binding.Owner != owner)
                    continue;

                binding.Active = false;
                this.bindings.Remove(name);
                released.Add(name);
            }
        }

        if (released.Count > 0)
            removed(released);
    }
}

internal sealed class RecipeTypeBinding(
    PluginContainer owner,
    string identifier,
    Func<string, JsonElement, object> read,
    Func<object, IReadOnlyList<ItemStack?>, bool> matches)
{
    internal PluginContainer Owner { get; } = owner;
    internal string Identifier { get; } = identifier;
    internal volatile bool Active = true;

    internal object Read(string name, JsonElement json) => read(name, json);
    internal bool Matches(object value, IReadOnlyList<ItemStack?> inputs) => matches(value, inputs);
}

internal sealed record PluginRecipeValue(
    PluginContainer Owner, string Identifier, RecipeTypeBinding Binding, object Value);
