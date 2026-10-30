using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Networking;
using Obsidian.API.World.Features;

namespace Obsidian.API.Plugins;
public interface IPluginRegistry
{
    /// <summary>Registers a synced custom statistic in the plugin's namespace during ConfigureRegistry.</summary>
    public int RegisterCustomStatistic(string identifier) =>
        throw new NotSupportedException("Custom statistics are unavailable.");

    /// <summary>
    /// Registers server advancement conditions during ConfigureRegistry; unloaded triggers are inert.
    /// </summary>
    public Advancements.AdvancementTrigger<TInstance> RegisterAdvancementTrigger<TInstance>(
        string identifier, Advancements.AdvancementConditionsCodec<TInstance> conditions) =>
        throw new NotSupportedException("Advancement triggers are unavailable.");

    /// <summary>Registers a server portal in the plugin's namespace; its behaviour is removed on unload.</summary>
    /// <remarks>
    /// Portals are the server's: the Obsidian client, running a <c>both</c> mod's ConfigureRegistry, keeps only the
    /// portal's identity, and draws its blocks like any.
    /// </remarks>
    public World.PortalType RegisterPortal(string identifier, World.PortalProperties properties) => new(identifier);

    /// <summary>Registers persistent world/level data. Call during ConfigureRegistry.</summary>
    public Data.SavedDataType<TValue> RegisterSavedData<TValue>(string identifier, ComponentCodec<TValue> codec);

    /// <summary>
    /// Registers detached entity, player and chunk data; a network codec enables optional client sync.
    /// </summary>
    public Data.AttachmentType<TValue> RegisterAttachment<TValue>(
        string identifier,
        ComponentCodec<TValue> codec,
        ComponentNetworkCodec<TValue>? networkCodec = null,
        bool copyOnDeath = false);

    /// <summary>
    /// Declares a sound event in this plugin's namespace during ConfigureRegistry on either side. Use the returned
    /// value with SoundEffectBuilder.Create and IPlayer.SendSoundAsync, or with the client's sound API.
    /// Sounds use vanilla's inline holders (identifier and optional fixed range), so no ids or registry sync are
    /// needed and a sound-only plugin does not prevent vanilla clients joining. Clients need its resource pack
    /// to hear it. Unload removes the declaration; re-register on reload to replace its range.
    /// </summary>
    public SoundEvent RegisterSound(string identifier, float? fixedRange = null);

    /// <summary>
    /// Registers a server-side recipe type in this plugin's namespace during ConfigureRegistry. Recipe files name
    /// it in type; its serializer and recipes stop matching on unload. Vanilla clients receive no custom types.
    /// </summary>
    public RecipeType<TRecipe> RegisterRecipeType<TRecipe>(string identifier, IRecipeSerializer<TRecipe> serializer)
        where TRecipe : class => throw new NotSupportedException("This side doesn't support server recipe types.");

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
    /// Registers a particle type without options, like a NeoForge mod's <c>SimpleParticleType</c>: its identifier is
    /// in the plugin's namespace, and it gets the next free particle type id. Send it to players with
    /// <see cref="ILevel.SpawnParticles"/>. Registering it again, e.g. when the plugin reloads, gives back the same
    /// type.
    /// </summary>
    /// <param name="identifier">The particle type's identifier, like <c>myplugin:spark</c>.</param>
    /// <returns>The particle type, which is the particle to send.</returns>
    /// <remarks>
    /// Like a plugin's blocks, its particle types need NeoForge or Fabric clients with a matching mod, which draws
    /// them, and stay registered until the server stops.
    /// </remarks>
    /// <exception cref="ArgumentException">The identifier isn't in the plugin's namespace, or isn't valid.</exception>
    public ParticleDefinition RegisterParticle(string identifier);

    /// <summary>
    /// Registers a menu type, like a NeoForge mod's <c>MenuType</c>: a grid of slots above the player's inventory,
    /// whose identifier is in the plugin's namespace and which gets the next free menu id. Open it for a player with
    /// <see cref="IPlayer.OpenInventoryAsync"/> and a <see cref="MenuContainer"/>. Registering it again, e.g. when the
    /// plugin reloads, gives back the same type; its shape can't change until the server restarts.
    /// </summary>
    /// <param name="identifier">The menu type's identifier, like <c>myplugin:pouch</c>.</param>
    /// <param name="rows">How many rows of slots it has, from 1 to 6.</param>
    /// <param name="columns">How many slots each row has, from 1 to 9.</param>
    /// <returns>The menu type, to create containers of.</returns>
    /// <remarks>
    /// Like a plugin's blocks, its menu types need NeoForge or Fabric clients with a matching mod, which shows their
    /// screens, and stay registered until the server stops.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The identifier isn't in the plugin's namespace or isn't valid, or the menu's shape changed.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The rows or columns are out of range.</exception>
    public MenuDefinition RegisterMenu(string identifier, int rows, int columns = 9);

    /// <summary>
    /// Registers a block entity type, like a NeoForge mod's <c>BlockEntityType</c>: <paramref name="blocks"/> get a
    /// <typeparamref name="TBlockEntity"/> wherever they're set, which is saved, ticked and sent to clients (see
    /// <see cref="BlockEntities.BlockEntity"/>). Its identifier is in the plugin's namespace, and it gets the next free
    /// block entity type id. Registering it again, e.g. when the plugin reloads, gives back the same type with the new
    /// blocks and class.
    /// </summary>
    /// <typeparam name="TBlockEntity">The block entity, created with its parameterless constructor.</typeparam>
    /// <param name="identifier">The type's identifier, like <c>myplugin:counter</c>.</param>
    /// <param name="blocks">The plugin's blocks that have the block entity.</param>
    /// <returns>The block entity type.</returns>
    /// <remarks>
    /// Like a plugin's blocks, its block entity types need NeoForge or Fabric clients with a matching mod, and stay
    /// registered until the server stops. When the plugin unloads, its block entities are saved into their chunks and
    /// let go of; they're kept as saved until the plugin loads again, which creates them again from what was saved.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The identifier isn't in the plugin's namespace or isn't valid, or a block isn't one of the plugin's, or has
    /// another block entity type.
    /// </exception>
    public BlockEntities.BlockEntityType RegisterBlockEntity<TBlockEntity>(
        string identifier,
        params IEnumerable<BlockType> blocks)
        where TBlockEntity : BlockEntities.BlockEntity, new() =>
        throw new NotSupportedException("This side doesn't support block entity types.");

    /// <summary>
    /// Registers a data component type, like a NeoForge mod's <c>DataComponentType</c>: stacks hold values of it (see
    /// <see cref="ItemStack.Set"/>), saved with <paramref name="codec"/> under its identifier beside vanilla's
    /// components. Its identifier is in the plugin's namespace, and it gets the next free id after vanilla's types.
    /// Registering it again, e.g. when the plugin reloads, gives back a type with the same id and the new codecs.
    /// </summary>
    /// <param name="identifier">The type's identifier, like <c>myplugin:charge</c>.</param>
    /// <param name="codec">Saves and loads the type's values.</param>
    /// <param name="networkCodec">
    /// Sends the type's values to clients, whose mod registers the same type with the same codec. Without one, the
    /// type is the server's own, like vanilla's components that aren't synced: clients never get its values.
    /// </param>
    /// <returns>The type, to read and set its values on stacks with.</returns>
    /// <remarks>
    /// A type with a network codec is synced to clients like a plugin's items, so only NeoForge and Fabric clients
    /// with a matching mod can join, and stays synced until the server stops. When the plugin unloads, stacks keep its
    /// values as they were saved, saving and loading them unchanged, and its codecs read them again once it's back.
    /// </remarks>
    /// <exception cref="ArgumentException">The identifier isn't in the plugin's namespace, or isn't valid.</exception>
    public PluginComponentType<TValue> RegisterComponentType<TValue>(
        string identifier,
        ComponentCodec<TValue> codec,
        ComponentNetworkCodec<TValue>? networkCodec = null) =>
        throw new NotSupportedException("This side doesn't support data component types.");

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
    /// plugin's. Worldgen types run on the server's generation threads, which every level shares: don't block them
    /// waiting for asynchronous work, whose continuations would wait for the thread blocked.
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
    /// <c>predicate_type</c>, <c>processor_type</c>), like NeoForge's int provider, density function or biome source
    /// types: features', noise settings' and level stems' JSON can then name it wherever a
    /// <typeparamref name="TBase"/> is expected. The rules are <see cref="RegisterFeatureType{TFeature}"/>'s, and one
    /// identifier may name types of different kinds.
    /// </summary>
    /// <typeparam name="TBase">
    /// The kind of value, an interface or abstract class: <see cref="IIntProvider"/>, <see cref="IFloatProvider"/>,
    /// <see cref="IHeightProvider"/>, <see cref="IBlockStateProvider"/>, <see cref="IBlockPredicate"/>,
    /// <see cref="IRuleTest"/>, a tree part (<see cref="World.Features.Trees.TrunkPlacer"/>,
    /// <see cref="World.Features.Trees.FoliagePlacer"/>, <see cref="World.Features.Trees.RootPlacer"/>,
    /// <see cref="World.Features.Trees.TreeDecorator"/>, <see cref="World.Features.Trees.FeatureSize"/>),
    /// <see cref="World.Structures.StructureProcessor"/>, a structure type
    /// (<see cref="World.Structures.CustomStructure"/>), a pool element type
    /// (<see cref="World.Structures.CustomPoolElement"/>), <see cref="IDensityFunction"/>, <see cref="ISurfaceRule"/>
    /// (for an <see cref="ICustomSurfaceRule"/>), <see cref="ISurfaceCondition"/> (for an
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

    /// <summary>
    /// Registers a level generator that worlds and dimensions name by its identifier: a world's <c>Generator</c> in
    /// <c>worlds.json</c>, or a child dimension's name there. Each level using it gets its own instance, created with
    /// the plugin's services, and saves the identifier.
    /// </summary>
    /// <typeparam name="TGenerator">The generator. Its <see cref="ILevelGenerator.Id"/> isn't used.</typeparam>
    /// <param name="identifier">The generator's identifier, in the plugin's namespace.</param>
    /// <remarks>
    /// Register generators in <see cref="PluginBase.ConfigureRegistry"/>. A level using one stops generating new
    /// chunks when the plugin unloads, or when the generator throws, and generates again with a new instance once the
    /// plugin loads again; until then, and after a restart without the plugin, its chunks already generated still load.
    /// Each call into the generator holds a call into the plugin, a chunk at a time.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The identifier isn't in the plugin's namespace, or is already a generator: of the plugin, or of another plugin
    /// that registered it with <see cref="World.ILevelFactory"/>. Another instance of the plugin's, like the one a
    /// reload replaces, is replaced instead.
    /// </exception>
    /// <exception cref="InvalidOperationException">The plugin has already loaded, or is unloading.</exception>
    public IPluginRegistry RegisterGenerator<TGenerator>(string identifier)
        where TGenerator : class, ILevelGenerator;

    /// <summary>
    /// Registers a biome region that swaps some of a dimension's biomes for others, like a
    /// <c>data/&lt;namespace&gt;/biome_region</c> file with <c>replace</c>, which it's read as once the plugin's
    /// biomes are registered.
    /// </summary>
    /// <param name="identifier">The region's identifier, in the plugin's namespace.</param>
    /// <param name="dimension">The dimension's level stem, like <c>minecraft:overworld</c>.</param>
    /// <param name="weight">How often the region is picked, from 1 to 1000, against the dimension's own layout's
    /// 10.</param>
    /// <param name="replace">The biome generated instead of each biome, or of each biome of a tag
    /// (<c>#c:is_plains</c>).</param>
    /// <remarks>
    /// Register regions in <see cref="PluginBase.ConfigureRegistry"/>. A region that can't be read, like one naming a
    /// biome that doesn't exist, is logged and left out, as a file would be. Servers that don't generate worlds,
    /// like the Obsidian client, ignore regions.
    /// </remarks>
    /// <exception cref="ArgumentException">The identifier isn't in the plugin's namespace.</exception>
    /// <exception cref="InvalidOperationException">The plugin has already loaded, or is unloading.</exception>
    public IPluginRegistry RegisterBiomeRegion(
        string identifier,
        string dimension,
        int weight,
        IReadOnlyDictionary<string, string> replace) => this;

    /// <summary>
    /// Registers a biome region with a parameter list of its own, like a <c>data/&lt;namespace&gt;/biome_region</c>
    /// file with <c>biomes</c>, which it's read as once the plugin's biomes are registered: where the region is, its
    /// biomes are picked from the climate, so every climate should find a fitting biome, oceans and caves included.
    /// </summary>
    /// <param name="identifier">The region's identifier, in the plugin's namespace.</param>
    /// <param name="dimension">The dimension's level stem, like <c>minecraft:overworld</c>.</param>
    /// <param name="weight">How often the region is picked, from 1 to 1000, against the dimension's own layout's
    /// 10.</param>
    /// <param name="biomes">The region's biomes and their climates, in order, which decides ties.</param>
    /// <remarks>The rules are those of the other overload.</remarks>
    /// <exception cref="ArgumentException">The identifier isn't in the plugin's namespace.</exception>
    /// <exception cref="InvalidOperationException">The plugin has already loaded, or is unloading.</exception>
    public IPluginRegistry RegisterBiomeRegion(
        string identifier,
        string dimension,
        int weight,
        IEnumerable<World.Generator.ClimateBiome> biomes) => this;

    /// <summary>
    /// Adds a potion mix to brewing stands, like NeoForge's brewing recipes: <paramref name="ingredient"/> brews a
    /// bottle of <paramref name="from"/> (a potion, splash or lingering potion) into the same kind of bottle of
    /// <paramref name="to"/>. The ingredient slot takes the ingredient, and shift-clicking sends it there.
    /// </summary>
    /// <remarks>
    /// Brewing recipes (vanilla's <c>minecraft:brewing</c> recipes and plugins' recipe files) are tried first, then
    /// plugins' mixes in the order they were added, so a mix can't change what a recipe brews. The mix is removed when
    /// the plugin unloads; a reload adds it again. Servers that don't brew, like the Obsidian client, ignore mixes.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public IPluginRegistry AddPotionMix(Potion from, Item ingredient, Potion to) => this;

    /// <summary>
    /// Adds a container mix to brewing stands: <paramref name="ingredient"/> brews a <paramref name="from"/> bottle
    /// into a <paramref name="to"/> bottle of the same potion, like gunpowder brews potions into splash potions. The
    /// rules are <see cref="AddPotionMix"/>'s.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="from"/> or <paramref name="to"/> isn't a potion, splash potion or lingering potion, the bottles
    /// brewing stands take.
    /// </exception>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public IPluginRegistry AddContainerMix(Item from, Item ingredient, Item to) => this;

    /// <summary>
    /// Registers a creative tab, like a NeoForge mod's <c>CreativeModeTab</c>: the Obsidian client shows it in the
    /// creative inventory after vanilla's tabs (or <see cref="CreativeTabProperties.After"/> another), on further
    /// pages once vanilla's ten places are full, and its items in the search tab. Its identifier is in the plugin's
    /// namespace. The tab and the plugin's additions to tabs (see <see cref="AddToCreativeTab"/>) are removed when the
    /// plugin unloads; a reload registers them again.
    /// </summary>
    /// <remarks>
    /// Like vanilla's and NeoForge's, creative tabs are the client's: they aren't synced, so servers only check the
    /// registration, and a player in creative mode can set any item in their inventory whatever the tabs show, which
    /// a server accepts like vanilla's. A plugin's items appear in no tab unless it puts them in one.
    /// </remarks>
    /// <param name="identifier">The tab's identifier, like <c>myplugin:rubies</c>.</param>
    /// <param name="properties">The tab's title, icon, place and items.</param>
    /// <exception cref="ArgumentException">
    /// The identifier isn't in the plugin's namespace or isn't valid, or the plugin already registered it (checked on
    /// the client).
    /// </exception>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public IPluginRegistry RegisterCreativeTab(string identifier, CreativeTabProperties properties) => this;

    /// <summary>
    /// Adds items to a creative tab, vanilla's (like <c>minecraft:ingredients</c>) or a plugin's, like NeoForge's
    /// <c>BuildCreativeModeTabContentsEvent</c>: at the end of the tab, or right after the last of
    /// <paramref name="after"/>'s stacks when the tab shows it. The search tab shows every tab's items, so items
    /// added to the hotbar, search or inventory tabs, or to a tab that isn't registered, aren't shown. The rules are
    /// <see cref="RegisterCreativeTab"/>'s.
    /// </summary>
    /// <param name="tab">The tab's identifier.</param>
    /// <param name="items">The items to add, in order.</param>
    /// <param name="after">The item to add them after, or null for the end.</param>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public IPluginRegistry AddToCreativeTab(string tab, IEnumerable<Item> items, Item? after = null) => this;
}


//TODO better name maybe??
public delegate ValueTask ValueTaskContextDelegate<TContext>(TContext context);
