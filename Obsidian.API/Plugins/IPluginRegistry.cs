using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Networking;
using Obsidian.API.World.Features;

namespace Obsidian.API.Plugins;
public interface IPluginRegistry
{
    public IPluginRegistry RegisterCommandArgumentHandler<T>(BaseArgumentParser<T> parser);

    public IPluginRegistry MapCommands();
    public IPluginRegistry MapCommand(string name, Delegate contextDelegate);
    public IPluginRegistry MapCommand(string name, ValueTaskContextDelegate<CommandContext> contextDelegate);

    public IPluginRegistry MapEvent<TEventArgs>(ValueTaskContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low)
        where TEventArgs : BaseMinecraftEventArgs;
    public IPluginRegistry MapEvent(Delegate contextDelegate, Priority priority = Priority.Low);
    public IPluginRegistry MapEvents();

    /// <summary>
    /// Registers a payload channel for <typeparamref name="TPayload"/>, negotiated with NeoForge clients during
    /// configuration, and checked against the channels Fabric clients register. The registration is removed when the
    /// plugin unloads.
    /// </summary>
    /// <param name="phase">The connection phase the channel is used in.</param>
    /// <param name="direction">Which way the channel carries payloads.</param>
    /// <param name="handler">
    /// Handles payloads clients send on the channel; required to receive them, and not allowed for a clientbound
    /// channel.
    /// </param>
    /// <param name="version">
    /// The channel version; a NeoForge client mod must declare the same one. Fabric channels have no version, so when a
    /// reload changes the payload's encoding, change the channel id too.
    /// </param>
    /// <param name="optional">Whether clients without the channel may still connect.</param>
    public IPluginRegistry MapPayload<TPayload>(PayloadPhase phase, PayloadDirection direction,
        PayloadHandler<TPayload>? handler = null, string version = "1", bool optional = false)
        where TPayload : ICustomPayload<TPayload>;

    /// <summary>
    /// Registers an item, like a NeoForge mod's item: its identifier is in the plugin's namespace (its id), and it gets
    /// the next free item id. Registering it again, e.g. when the plugin reloads, gives back the same item with the
    /// new properties and behaviour.
    /// </summary>
    /// <param name="identifier">The item's identifier, like <c>myplugin:ruby</c>.</param>
    /// <param name="properties">The item's properties and behaviour; the defaults if null.</param>
    /// <returns>The item, to create stacks of.</returns>
    /// <remarks>
    /// Clients without the plugin can't show its items, so once a plugin registered items, only NeoForge and Fabric
    /// clients with a matching mod can join. When the plugin unloads, its items lose their behaviour but stay
    /// registered, and synced to clients, until the server stops: stacks of them may still be in inventories and the
    /// world.
    /// </remarks>
    /// <exception cref="ArgumentException">The identifier isn't in the plugin's namespace, or isn't valid.</exception>
    public Item RegisterItem(string identifier, ItemProperties? properties = null);

    /// <summary>
    /// Registers a block, like a NeoForge mod's block: its identifier is in the plugin's namespace, it gets the next
    /// free block id and its states the next free state ids, and unless <see cref="BlockProperties.Item"/> is null an
    /// item with the same identifier that places it. Registering it again, e.g. when the plugin reloads, gives back the
    /// same block with the new properties and behaviour; its state properties can't change until the server restarts.
    /// </summary>
    /// <param name="identifier">The block's identifier, like <c>myplugin:ruby_block</c>.</param>
    /// <param name="properties">The block's properties and behaviour; the defaults if null.</param>
    /// <returns>The block, to get its states from.</returns>
    /// <remarks>
    /// Like a plugin's items, its blocks need NeoForge or Fabric clients with a matching mod, and stay registered until
    /// the server stops. A world saved with them loads them as air once the plugin is gone.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The identifier isn't in the plugin's namespace or isn't valid, or the block's state properties changed.
    /// </exception>
    public BlockType RegisterBlock(string identifier, BlockProperties? properties = null);

    /// <summary>
    /// Registers an entity type, like a NeoForge mod's: its identifier is in the plugin's namespace, and it gets the
    /// next free entity type id. Its entities are mobs, projectiles or objects (see <see cref="Entities.EntityKind"/>),
    /// spawned with <see cref="ILevel.SpawnEntity(VectorD, Entities.EntityDefinition, VectorD, IEntity?)"/>.
    /// Registering it again, e.g. when the plugin reloads, gives back the same type with the new properties and
    /// behaviour; its kind can't change until the server restarts.
    /// </summary>
    /// <remarks>
    /// Like a plugin's blocks, its entity types need NeoForge or Fabric clients with a matching mod, whose entity is
    /// of the same kind, and stay registered until the server stops. Saved entities of a type whose plugin is gone are
    /// kept as they were saved.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The identifier isn't in the plugin's namespace or isn't valid, or the type's kind changed.
    /// </exception>
    public Entities.EntityDefinition RegisterEntity(string identifier, Entities.EntityProperties? properties = null);

    /// <summary>
    /// Adds entries to a tag, vanilla's or a new one, like a tag file in the plugin's <c>data/</c> folder would: the
    /// entries are identifiers, or other tags' identifiers after a <c>#</c>, and the tag fails (keeping its earlier
    /// entries) when one doesn't exist. The entries are removed when the plugin unloads.
    /// </summary>
    /// <remarks>
    /// Tag files are the usual way, like a NeoForge or Fabric mod's data pack:
    /// <c>data/&lt;namespace&gt;/tags/&lt;registry&gt;/&lt;path&gt;.json</c> with <c>values</c>, <c>replace</c> and
    /// NeoForge's <c>remove</c>. Clients get the tags when they join.
    /// </remarks>
    /// <param name="registry">The tag's registry: <c>block</c>, <c>item</c> or <c>entity_type</c>.</param>
    /// <param name="tag">The tag's identifier, like <c>minecraft:mineable/pickaxe</c>.</param>
    /// <param name="entries">The entries to add, like <c>myplugin:ruby_ore</c> or <c>#c:ores</c>.</param>
    /// <exception cref="ArgumentException">The registry isn't supported, or an identifier isn't valid.</exception>
    public IPluginRegistry AddToTag(string registry, string tag, params IEnumerable<string> entries);

    /// <summary>
    /// Registers a feature type, like a NeoForge mod's <c>Feature</c>: the plugin's configured feature files (and
    /// other plugins') name it in their <c>type</c>, and its <c>config</c> object sets its properties.
    /// </summary>
    /// <typeparam name="TFeature">
    /// The feature, a class with a parameterless constructor (public or not) whose
    /// <see cref="ConfiguredFeatureBase.Type"/> returns <paramref name="identifier"/>.
    /// </typeparam>
    /// <param name="identifier">The type's identifier, in the plugin's namespace, like <c>myplugin:pillar</c>.</param>
    /// <remarks>
    /// Register worldgen types in <see cref="PluginBase.ConfigureRegistry"/>, before the plugin's data is read. They're
    /// gone when the plugin unloads, with the features made of them, and a reload registers them again. Feature
    /// configs are read like vanilla's: <c>snake_case</c> keys set the matching settable properties, and properties
    /// typed as worldgen interfaces (<see cref="IIntProvider"/>, <see cref="IBlockStateProvider"/>,
    /// <see cref="IBlockPredicate"/>, <see cref="IRuleTest"/>, ...) take any type of that kind, vanilla's or a
    /// plugin's.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The identifier isn't in the plugin's namespace, or is already a feature type of the plugin; or the type can't be
    /// created.
    /// </exception>
    /// <exception cref="InvalidOperationException">The plugin has already loaded, or is unloading.</exception>
    public IPluginRegistry RegisterFeatureType<TFeature>(string identifier)
        where TFeature : ConfiguredFeatureBase;

    /// <summary>
    /// Registers a placement modifier type, like a NeoForge mod's <c>PlacementModifierType</c>: placed feature files
    /// name it in a <c>placement</c> entry's <c>type</c>, and the entry's other keys set its properties. The rules are
    /// <see cref="RegisterFeatureType{TFeature}"/>'s.
    /// </summary>
    /// <typeparam name="TModifier">
    /// The modifier, usually a <see cref="SinglePlacementModifierBase"/> or <see cref="PlacementFilterBase"/>.
    /// </typeparam>
    /// <param name="identifier">The type's identifier, in the plugin's namespace.</param>
    public IPluginRegistry RegisterPlacementModifierType<TModifier>(string identifier)
        where TModifier : PlacementModifierBase;

    /// <summary>
    /// Registers a type of another kind of worldgen value that data chooses by its <c>type</c> (or
    /// <c>predicate_type</c>), like NeoForge's int provider, density function or biome source types: features', noise
    /// settings' and level stems' JSON can then name it wherever a <typeparamref name="TBase"/> is expected. The rules
    /// are <see cref="RegisterFeatureType{TFeature}"/>'s, and one identifier may name types of different kinds.
    /// </summary>
    /// <typeparam name="TBase">
    /// The kind of value, an interface or abstract class: <see cref="IIntProvider"/>, <see cref="IFloatProvider"/>,
    /// <see cref="IHeightProvider"/>, <see cref="IBlockStateProvider"/>, <see cref="IBlockPredicate"/>,
    /// <see cref="IRuleTest"/>, <see cref="IDensityFunction"/>, <see cref="ISurfaceRule"/> (for an
    /// <see cref="ICustomSurfaceRule"/>), <see cref="ISurfaceCondition"/> (for an
    /// <see cref="ICustomSurfaceCondition"/>), <see cref="World.Generator.BiomeSourceBase"/>, or one of the plugin's
    /// own that its features' properties use.
    /// </typeparam>
    /// <typeparam name="TValue">The type to register.</typeparam>
    /// <param name="identifier">The type's identifier, in the plugin's namespace.</param>
    /// <remarks>
    /// A density function whose properties hold density functions, noises or splines must implement
    /// <see cref="IDensityFunction.MapAll"/> to map them, as vanilla's do: generation seeds noises and binds caches
    /// through it.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// As for <see cref="RegisterFeatureType{TFeature}"/>, or <typeparamref name="TBase"/> is a concrete class; a
    /// surface rule or condition type doesn't implement <see cref="ICustomSurfaceRule"/> or
    /// <see cref="ICustomSurfaceCondition"/>; or a density function with functions in it doesn't implement
    /// <see cref="IDensityFunction.MapAll"/>.
    /// </exception>
    public IPluginRegistry RegisterWorldgenType<TBase, TValue>(string identifier)
        where TValue : class, TBase;
}


//TODO better name maybe??
public delegate ValueTask ValueTaskContextDelegate<TContext>(TContext context);
