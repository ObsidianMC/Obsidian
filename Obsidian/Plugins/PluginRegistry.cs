using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Networking;
using Obsidian.API.Plugins;
using Obsidian.API.World.Generator;
using Obsidian.Commands.Framework;
using Obsidian.Services;
using Obsidian.WorldData.Features;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Plugins;
public sealed class PluginRegistry(PluginManager pluginManager, EventDispatcher eventDispatcher, 
    CommandHandler commandHandler) : IPluginRegistry
{
    private readonly PluginManager pluginManager = pluginManager;
    private readonly EventDispatcher eventDispatcher = eventDispatcher;
    private readonly CommandHandler commandHandler = commandHandler;

    public IPluginRegistry MapCommand(string name, Delegate handler)
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.commandHandler.RegisterCommand(container, name, handler);
        return this;
    }

    public IPluginRegistry MapCommand(string name, ValueTaskContextDelegate<CommandContext> contextDelegate)
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.commandHandler.RegisterCommand(container, name, contextDelegate);
        return this;
    }
    
    public IPluginRegistry MapCommands()
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.commandHandler.RegisterCommands(container);

        return this;
    }

    public IPluginRegistry MapEvent<TEventArgs>(ValueTaskContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low)
        where TEventArgs : BaseMinecraftEventArgs
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.eventDispatcher.RegisterEvent(container, contextDelegate, priority);

        return this;
    }

    public IPluginRegistry MapEvent(Delegate handler, Priority priority = Priority.Low) 
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.eventDispatcher.RegisterEvent(container, handler, priority);

        return this;
    }

    public IPluginRegistry MapEvents()
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);

        this.eventDispatcher.RegisterEvents(container);

        return this;
    }

    public IPluginRegistry MapPayload<TPayload>(PayloadPhase phase, PayloadDirection direction,
        PayloadHandler<TPayload>? handler = null, string version = "1", bool optional = false)
        where TPayload : ICustomPayload<TPayload>
    {
        var asm = Assembly.GetCallingAssembly();

        var container = this.pluginManager.GetPluginContainerByAssembly(asm);
        var server = (Server)this.pluginManager.server;

        server.PayloadChannels.Register(container, phase, direction, handler, version, optional);

        return this;
    }

    public Item RegisterItem(string identifier, ItemProperties? properties = null)
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        return RegisterItem(container, identifier, properties ?? new ItemProperties());
    }

    public BlockType RegisterBlock(string identifier, BlockProperties? properties = null)
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        return RegisterBlock(container, identifier, properties);
    }

    public API.Entities.EntityDefinition RegisterEntity(
        string identifier,
        API.Entities.EntityProperties? properties = null)
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        return RegisterEntity(container, identifier, properties);
    }

    public ParticleDefinition RegisterParticle(string identifier)
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        return RegisterParticle(container, identifier);
    }

    public MenuDefinition RegisterMenu(string identifier, int rows, int columns = 9)
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        return RegisterMenu(container, identifier, rows, columns);
    }

    public API.BlockEntities.BlockEntityType RegisterBlockEntity<TBlockEntity>(
        string identifier,
        params IEnumerable<BlockType> blocks)
        where TBlockEntity : API.BlockEntities.BlockEntity, new()
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        return RegisterBlockEntity(container, identifier, blocks, static () => new TBlockEntity());
    }

    public PluginComponentType<TValue> RegisterComponentType<TValue>(
        string identifier,
        ComponentCodec<TValue> codec,
        ComponentNetworkCodec<TValue>? networkCodec = null)
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        return RegisterComponentType(container, identifier, codec, networkCodec);
    }

    // The content registrations below are shared with the Obsidian client, which registers its mods' content the
    // same way, so that it gets the same ids as a server running the same mods.

    /// <summary>
    /// Registers <paramref name="container"/>'s data component type with its codecs.
    /// </summary>
    internal static PluginComponentType<TValue> RegisterComponentType<TValue>(
        PluginContainer container,
        string identifier,
        ComponentCodec<TValue> codec,
        ComponentNetworkCodec<TValue>? networkCodec)
    {
        CheckIdentifier(container, identifier);
        ArgumentNullException.ThrowIfNull(codec);

        var entry = DataComponentTypesRegistry.Register(identifier);

        if (!DataComponentTypesRegistry.TryBind(entry, codec, networkCodec, container, () => container.IsClosing))
            throw new InvalidOperationException($"{container.Info.Name} is unloading.");

        return new PluginComponentType<TValue>(entry.Type, entry.Identifier, codec);
    }

    /// <summary>
    /// Registers <paramref name="container"/>'s block entity type for its blocks, created with
    /// <paramref name="factory"/>.
    /// </summary>
    internal static API.BlockEntities.BlockEntityType RegisterBlockEntity(
        PluginContainer container,
        string identifier,
        IEnumerable<BlockType> blocks,
        Func<API.BlockEntities.BlockEntity> factory)
    {
        CheckIdentifier(container, identifier);

        var covered = blocks.ToList();
        foreach (var block in covered)
        {
            if (block.Owner != container)
                throw new ArgumentException($"{block} isn't a block of {container.Info.Name}.", nameof(blocks));
        }

        var type = BlockEntityTypesRegistry.Register(identifier, covered);

        if (!BlockEntityTypesRegistry.TryBind(type, factory, container, () => container.IsClosing))
            throw new InvalidOperationException($"{container.Info.Name} is unloading.");

        return type;
    }

    /// <summary>
    /// Registers <paramref name="container"/>'s block, and the item that places it unless it has none.
    /// </summary>
    internal static BlockType RegisterBlock(PluginContainer container, string identifier, BlockProperties? properties)
    {
        CheckIdentifier(container, identifier);

        properties ??= new BlockProperties();
        ArgumentOutOfRangeException.ThrowIfNegative(properties.LightEmission, nameof(properties));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(properties.LightEmission, 15, nameof(properties));

        var block = BlocksRegistry.Register(identifier, properties);

        if (!BlocksRegistry.TryBind(block, properties, container, () => container.IsClosing))
            throw new InvalidOperationException($"{container.Info.Name} is unloading.");

        if (properties.Item is ItemProperties itemProperties)
        {
            var item = RegisterItem(container, identifier, itemProperties);
            item.Block = block;
            block.Item = item;
        }
        else if (block.Item is Item previous)
        {
            // Reloaded without its item: the item stays registered for stacks of it, but no longer places the block.
            previous.Block = null;
            block.Item = null;
        }

        return block;
    }

    /// <summary>
    /// Registers <paramref name="container"/>'s entity type.
    /// </summary>
    internal static API.Entities.EntityDefinition RegisterEntity(
        PluginContainer container,
        string identifier,
        API.Entities.EntityProperties? properties)
    {
        CheckIdentifier(container, identifier);

        properties ??= new API.Entities.EntityProperties();
        if (properties.Width <= 0 || properties.Height <= 0 || properties.MaxHealth <= 0 || properties.Gravity < 0)
            throw new ArgumentException("An entity needs a size, health and no negative gravity.", nameof(properties));

        var definition = EntityDefinitionsRegistry.Register(identifier, properties.Kind);

        if (!EntityDefinitionsRegistry.TryBind(definition, properties, container, () => container.IsClosing))
            throw new InvalidOperationException($"{container.Info.Name} is unloading.");

        return definition;
    }

    /// <summary>
    /// Registers <paramref name="container"/>'s particle type. It has no behaviour, so there's nothing to bind.
    /// </summary>
    internal static ParticleDefinition RegisterParticle(PluginContainer container, string identifier)
    {
        CheckIdentifier(container, identifier);

        return ParticleTypesRegistry.Register(identifier);
    }

    /// <summary>
    /// Registers <paramref name="container"/>'s menu type. It has no behaviour, so there's nothing to bind, but the
    /// plugin owns it until it unloads, which closes its open menus.
    /// </summary>
    internal static MenuDefinition RegisterMenu(PluginContainer container, string identifier, int rows, int columns)
    {
        CheckIdentifier(container, identifier);

        var menu = MenuTypesRegistry.Register(identifier, rows, columns);

        // Checked inside the gate, so that an unloading plugin's menu type stays closed (see
        // PluginManager.CloseMenusAsync).
        lock (menu.Gate)
        {
            if (container.IsClosing)
                throw new InvalidOperationException($"{container.Info.Name} is unloading.");

            menu.Owner = container;
            menu.IsOpenable = true;
        }

        return menu;
    }

    public IPluginRegistry AddToTag(string registry, string tag, params IEnumerable<string> entries)
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        container.TagFiles.Add(PluginTags.FromEntries(registry, tag, $"{container.Info.Id} (code)", entries));

        return this;
    }

    public IPluginRegistry RegisterFeatureType<TFeature>(string identifier)
        where TFeature : ConfiguredFeatureBase
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        RegisterWorldgenType(container, identifier, typeof(ConfiguredFeatureBase), typeof(TFeature));

        return this;
    }

    public IPluginRegistry RegisterPlacementModifierType<TModifier>(string identifier)
        where TModifier : PlacementModifierBase
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        RegisterWorldgenType(container, identifier, typeof(PlacementModifierBase), typeof(TModifier));

        return this;
    }

    public IPluginRegistry RegisterWorldgenType<TBase, TValue>(string identifier)
        where TValue : class, TBase
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        RegisterWorldgenType(container, identifier, typeof(TBase), typeof(TValue));

        return this;
    }

    public IPluginRegistry RegisterGenerator<TGenerator>(string identifier)
        where TGenerator : class, ILevelGenerator
    {
        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        CheckIdentifier(container, identifier);

        // Levels look for it once the plugin loaded (see PluginManager.ResumeGenerators), so it's registered before.
        lock (container.WorldgenTypes)
        {
            if (!container.RegisteringWorldgenTypes || container.IsClosing)
            {
                throw new InvalidOperationException(
                    $"{container.Info.Name} can only register generators in ConfigureRegistry.");
            }

            // Taken by this plugin, or by another through ILevelFactory, which has no namespace rule.
            if (!PluginGenerators.TryRegister(container, identifier, typeof(TGenerator)))
                throw new ArgumentException($"{identifier} is already a registered generator.");
        }

        return this;
    }

    public IPluginRegistry RegisterBiomeRegion(
        string identifier,
        string dimension,
        int weight,
        IReadOnlyDictionary<string, string> replace)
    {
        ArgumentNullException.ThrowIfNull(replace);

        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        RegisterBiomeRegion(container, identifier, dimension, weight, writer =>
        {
            writer.WriteStartObject("replace");

            foreach (var (replaced, biome) in replace)
                writer.WriteString(replaced, biome);

            writer.WriteEndObject();
        });

        return this;
    }

    public IPluginRegistry RegisterBiomeRegion(
        string identifier,
        string dimension,
        int weight,
        IEnumerable<ClimateBiome> biomes)
    {
        ArgumentNullException.ThrowIfNull(biomes);

        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        RegisterBiomeRegion(container, identifier, dimension, weight, writer =>
        {
            writer.WriteStartArray("biomes");

            foreach (var entry in biomes)
            {
                writer.WriteStartObject();
                writer.WriteString("biome", entry.Biome);
                writer.WriteStartObject("parameters");
                Range("temperature", entry.Temperature);
                Range("humidity", entry.Humidity);
                Range("continentalness", entry.Continentalness);
                Range("erosion", entry.Erosion);
                Range("depth", entry.Depth);
                Range("weirdness", entry.Weirdness);
                writer.WriteNumber("offset", entry.Offset);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            void Range(string name, ClimateRange range)
            {
                writer.WriteStartArray(name);
                writer.WriteNumberValue(range.Min);
                writer.WriteNumberValue(range.Max);
                writer.WriteEndArray();
            }
        });

        return this;
    }

    public IPluginRegistry AddPotionMix(Potion from, Item ingredient, Potion to)
    {
        ArgumentNullException.ThrowIfNull(ingredient);

        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        BrewingRegistry.Add(container, new PotionMix(from, ingredient, to), () => container.IsClosing);

        return this;
    }

    public IPluginRegistry AddContainerMix(Item from, Item ingredient, Item to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(ingredient);
        ArgumentNullException.ThrowIfNull(to);

        var container = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());

        BrewingRegistry.Add(container, new ContainerMix(from, ingredient, to), () => container.IsClosing);

        return this;
    }

    // A region registered in code is kept as the JSON of a region file, and read with the plugin's region files once
    // its biomes are registered, so that it's checked and generates exactly like one.
    private static void RegisterBiomeRegion(
        PluginContainer container,
        string identifier,
        string dimension,
        int weight,
        Action<Utf8JsonWriter> writeLayout)
    {
        CheckIdentifier(container, identifier);

        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteString("dimension", dimension);
            writer.WriteNumber("weight", weight);
            writeLayout(writer);
            writer.WriteEndObject();
        }

        lock (container.WorldgenTypes)
        {
            if (!container.RegisteringWorldgenTypes || container.IsClosing)
            {
                throw new InvalidOperationException(
                    $"{container.Info.Name} can only register biome regions in ConfigureRegistry.");
            }

            container.RegisteredBiomeRegions.Add((identifier, json.ToArray()));
        }
    }

    // The plugin's feature files are read once it configured its registry, with the types it registered by then, and
    // read again whenever plugins change. Types registered later would only count from the next change, so they're
    // refused instead.
    private static void RegisterWorldgenType(PluginContainer container, string identifier, Type baseType, Type type)
    {
        CheckIdentifier(container, identifier);

        lock (container.WorldgenTypes)
        {
            if (!container.RegisteringWorldgenTypes || container.IsClosing)
            {
                throw new InvalidOperationException(
                    $"{container.Info.Name} can only register worldgen types in ConfigureRegistry.");
            }

            container.WorldgenTypes.Add(
                WorldgenTypeRegistration.Create(container.WorldgenTypes, identifier, baseType, type));
        }
    }

    /// <summary>
    /// Registers <paramref name="container"/>'s item.
    /// </summary>
    internal static Item RegisterItem(PluginContainer container, string identifier, ItemProperties properties)
    {
        CheckIdentifier(container, identifier);

        ArgumentOutOfRangeException.ThrowIfLessThan(properties.MaxStackSize, 1, nameof(properties));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(properties.MaxStackSize, 99, nameof(properties));

        var item = ItemsRegistry.Register(identifier);

        if (!ItemsRegistry.TryBind(item, properties, container, () => container.IsClosing))
            throw new InvalidOperationException($"{container.Info.Name} is unloading.");

        return item;
    }

    /// <summary>
    /// Checks that <paramref name="identifier"/> is in <paramref name="container"/>'s namespace and valid by vanilla's
    /// rules; the Obsidian client checks its mods' client registrations with it too.
    /// </summary>
    /// <exception cref="ArgumentException">It isn't.</exception>
    internal static void CheckIdentifier(PluginContainer container, string identifier)
    {
        // Like a NeoForge mod's, a plugin's content is in its own namespace, so plugins can't take each other's.
        if (!identifier.StartsWith($"{container.Info.Id}:", StringComparison.Ordinal))
            throw new ArgumentException($"{identifier} isn't in the plugin's namespace, {container.Info.Id}.");

        // Vanilla's identifier rules, which clients parse the identifier with.
        var path = identifier[(container.Info.Id.Length + 1)..];
        if (path.Length == 0
            || !container.Info.Id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-' or '.')
            || !path.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-' or '.' or '/'))
            throw new ArgumentException($"{identifier} isn't a valid identifier.");
    }

    public IPluginRegistry RegisterCommandArgumentHandler<T>(BaseArgumentParser<T> parser) 
    {
        this.commandHandler.TryAddArgumentParser(parser);

        return this;
    }
}
