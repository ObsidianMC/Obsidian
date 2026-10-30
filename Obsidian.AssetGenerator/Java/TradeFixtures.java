import com.google.gson.*;
import com.mojang.serialization.*;
import java.lang.reflect.Method;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.Executor;
import net.minecraft.core.HolderSet;
import net.minecraft.core.Registry;
import net.minecraft.core.RegistryAccess;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.RegistryDataLoader;
import net.minecraft.resources.RegistryOps;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.packs.PackType;
import net.minecraft.server.packs.repository.ServerPacksSource;
import net.minecraft.server.packs.resources.MultiPackResourceManager;
import net.minecraft.util.Unit;
import net.minecraft.util.context.ContextKey;
import net.minecraft.util.context.ContextMap;
import net.minecraft.world.RandomSequences;
import net.minecraft.network.syncher.EntityDataAccessor;
import net.minecraft.network.syncher.SynchedEntityData;
import net.minecraft.world.entity.npc.villager.AbstractVillager;
import net.minecraft.world.entity.npc.villager.Villager;
import net.minecraft.world.entity.npc.villager.VillagerData;
import net.minecraft.world.entity.npc.wanderingtrader.WanderingTrader;
import net.minecraft.world.item.trading.MerchantOffer;
import net.minecraft.world.item.trading.MerchantOffers;
import net.minecraft.world.item.trading.TradeSet;
import net.minecraft.world.item.trading.TradeSets;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.storage.loot.LootContext;
import net.minecraft.world.level.storage.loot.LootParams;
import net.minecraft.world.level.storage.loot.parameters.LootContextParams;

/**
 * Captures the offers vanilla's merchants draw from the 26.3 trade sets, for the fixed world seeds below. Usage: output
 * JSON.
 *
 * <p>Each world's merchants are created in order and share one {@code RandomSequences}, as the merchants of a server
 * do, so a later merchant continues the sequences an earlier one used. A villager is unlocked level by level, as when
 * it levels up. The trade sets, trades and tags are vanilla's own, loaded from the vanilla pack.
 *
 * <p>No world is started: the context has no origin, so explorer maps aren't made, as in a world without the
 * structures, and vanilla's filter then discards those trades. Everything else is vanilla's {@code VillagerTrade},
 * trade set and draw loop ({@code AbstractVillager.addOffersFromItemListings*}).
 */
public final class TradeFixtures {
    private record Merchant(String profession, String type, int levels) {}

    private static final long[] SEEDS = {12345L, -4172144997902289642L};

    private static final Merchant[] MERCHANTS = {
        new Merchant("armorer", "plains", 5),
        new Merchant("butcher", "desert", 5),
        new Merchant("cartographer", "plains", 5),
        new Merchant("cartographer", "swamp", 5),
        new Merchant("cleric", "jungle", 5),
        new Merchant("farmer", "savanna", 5),
        new Merchant("fisherman", "snow", 5),
        new Merchant("fletcher", "taiga", 5),
        new Merchant("leatherworker", "swamp", 5),
        new Merchant("librarian", "plains", 5),
        new Merchant("librarian", "desert", 5),
        new Merchant("mason", "jungle", 5),
        new Merchant("shepherd", "savanna", 5),
        new Merchant("toolsmith", "snow", 5),
        new Merchant("weaponsmith", "taiga", 5),
        new Merchant("wandering_trader", null, 0),
        new Merchant("wandering_trader", null, 0)
    };

    private static RegistryAccess access;
    private static DynamicOps<JsonElement> ops;
    private static ServerLevel level;

    public static void main(String[] arguments) throws Exception {
        access = loadRegistries();
        ops = RegistryOps.create(JsonOps.INSTANCE, access);
        level = emptyLevel();

        var worlds = new JsonArray();
        for (var seed : SEEDS) {
            worlds.add(captureWorld(seed));
        }

        var output = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create().toJson(worlds);
        Files.writeString(Path.of(arguments[0]), output + "\n");
        captureSavedVillager(Path.of(arguments[0]).resolveSibling("villager-saved-26.3.nbt"));
    }

    // The built-in registries and the vanilla pack's data pack registries, with their tags, as a server loads them.
    @SuppressWarnings("unchecked")
    private static RegistryAccess loadRegistries() {
        net.minecraft.SharedConstants.tryDetectVersion();
        net.minecraft.server.Bootstrap.bootStrap();
        var pack = ServerPacksSource.createVanillaPackSource().fullResources();
        var resources = new MultiPackResourceManager(PackType.SERVER_DATA, List.of(pack));

        var loadPendingTags = Vanilla.method("net.minecraft.tags.TagLoader", "loadPendingTags",
            "net.minecraft.server.packs.resources.ResourceManager", "net.minecraft.core.Registry");
        var registries = new ArrayList<Registry<?>>();
        BuiltInRegistries.REGISTRY.forEach(registries::add);
        for (var registry : List.copyOf(registries)) {
            var pending = (Optional<Registry.PendingTags<?>>) Vanilla.call(loadPendingTags, null, resources, registry);
            pending.ifPresent(Registry.PendingTags::apply);
        }

        var lookups = registries.stream().map(registry -> (Object) registry).toList();
        var dynamic = loadDynamic(resources, lookups);
        dynamic.registries().forEach(entry -> registries.add(entry.value()));

        var loaded = new RegistryAccess.ImmutableRegistryAccess(registries);
        VanillaDumper.bindComponents(loaded);
        return loaded;
    }

    @SuppressWarnings("unchecked")
    private static RegistryAccess.Frozen loadDynamic(MultiPackResourceManager resources, List<Object> lookups) {
        var load = Vanilla.method("net.minecraft.resources.RegistryDataLoader", "load",
            "net.minecraft.server.packs.resources.ResourceManager", "java.util.List", "java.util.List",
            "java.util.concurrent.Executor");
        Executor direct = Runnable::run;
        var future = (java.util.concurrent.CompletableFuture<RegistryAccess.Frozen>) Vanilla.call(
            load, null, resources, lookups, RegistryDataLoader.WORLD_REGISTRIES, direct);
        return future.join();
    }

    // A server level with only the registries: enchanting with levels reads them, and nothing else here touches the
    // level. World construction is unrelated to the capture.
    private static ServerLevel emptyLevel() throws Exception {
        var empty = (ServerLevel) allocate(ServerLevel.class);

        var registryField = Level.class.getDeclaredField("registryAccess");
        registryField.setAccessible(true);
        registryField.set(empty, access);
        return empty;
    }

    // An instance of a class without running its constructors: merchants can't be constructed without a world.
    private static Object allocate(Class<?> type) throws Exception {
        var unsafeField = sun.misc.Unsafe.class.getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = (sun.misc.Unsafe) unsafeField.get(null);
        return unsafe.allocateInstance(type);
    }

    // Gives an allocated villager synced data holding only its villager data, all that the merchant predicates'
    // villager/variant component reads (Villager.get).
    @SuppressWarnings("unchecked")
    private static void setVillagerData(Villager villager, VillagerData data) throws Exception {
        var accessorField = Villager.class.getDeclaredField("DATA_VILLAGER_DATA");
        accessorField.setAccessible(true);
        var accessor = (EntityDataAccessor<VillagerData>) accessorField.get(null);

        var items = new SynchedEntityData.DataItem<?>[accessor.id() + 1];
        items[accessor.id()] = new SynchedEntityData.DataItem<>(accessor, data);
        var constructor = Vanilla.constructor("net.minecraft.network.syncher.SynchedEntityData",
            "net.minecraft.network.syncher.SyncedDataHolder",
            "net.minecraft.network.syncher.SynchedEntityData$DataItem[]");
        var entityData = Vanilla.create(constructor, villager, items);

        var dataField = net.minecraft.world.entity.Entity.class.getDeclaredField("entityData");
        dataField.setAccessible(true);
        dataField.set(villager, entityData);
    }

    private static JsonObject captureWorld(long seed) throws Exception {
        var sequences = new RandomSequences();
        var merchants = new JsonArray();
        for (var merchant : MERCHANTS) {
            merchants.add(captureMerchant(merchant, sequences, seed));
        }

        var world = new JsonObject();
        world.addProperty("seed", seed);
        world.add("merchants", merchants);
        return world;
    }

    private static JsonObject captureMerchant(Merchant merchant, RandomSequences sequences, long seed)
        throws Exception {
        var row = new JsonObject();
        row.addProperty("profession", merchant.profession());
        if (merchant.type() != null) {
            row.addProperty("type", "minecraft:" + merchant.type());
        }

        var levels = new JsonArray();
        if (merchant.levels() == 0) {
            var trader = (WanderingTrader) allocate(WanderingTrader.class);
            var offers = new MerchantOffers();
            // WanderingTrader.updateTrades' order.
            addOffers(trader, offers, TradeSets.WANDERING_TRADER_BUYING, sequences, seed);
            addOffers(trader, offers, TradeSets.WANDERING_TRADER_UNCOMMON, sequences, seed);
            addOffers(trader, offers, TradeSets.WANDERING_TRADER_COMMON, sequences, seed);
            levels.add(describe(offers));
        } else {
            var villager = (Villager) allocate(Villager.class);
            var profession = access.lookupOrThrow(Registries.VILLAGER_PROFESSION)
                .getOrThrow(ResourceKey.create(Registries.VILLAGER_PROFESSION,
                    Identifier.withDefaultNamespace(merchant.profession())));
            var type = access.lookupOrThrow(Registries.VILLAGER_TYPE)
                .getOrThrow(ResourceKey.create(Registries.VILLAGER_TYPE,
                    Identifier.withDefaultNamespace(merchant.type())));
            for (var villagerLevel = 1; villagerLevel <= merchant.levels(); villagerLevel++) {
                var data = new VillagerData(type, profession, villagerLevel);
                setVillagerData(villager, data);

                // Villager.updateTrades: the profession's trade set for the level.
                var offers = new MerchantOffers();
                var tradeSet = profession.value().getTrades(villagerLevel);
                if (tradeSet != null) {
                    addOffers(villager, offers, tradeSet, sequences, seed);
                }

                levels.add(describe(offers));
            }
        }

        row.add("levels", levels);
        return row;
    }

    // AbstractVillager.addOffersFromTradeSet, with the world's random sequences and without an origin (see above).
    private static void addOffers(AbstractVillager merchant, MerchantOffers offers, ResourceKey<TradeSet> key,
        RandomSequences sequences, long seed) throws Exception {
        var tradeSet = access.lookupOrThrow(Registries.TRADE_SET).getValueOrThrow(key);
        var random = sequences.get(tradeSet.randomSequence().orElseThrow(), seed);

        var parameters = new HashMap<ContextKey<?>, Object>();
        parameters.put(LootContextParams.THIS_ENTITY, merchant);
        parameters.put(LootContextParams.ADDITIONAL_COST_COMPONENT_ALLOWED, Unit.INSTANCE);
        var map = Vanilla.create(Vanilla.constructor("net.minecraft.util.context.ContextMap", "java.util.Map"),
            parameters);
        var params = new LootParams(level, (ContextMap) map, Map.of(), 0);
        // LootContext.Builder.create would read the random sequence from the level's server.
        var contextConstructor = Vanilla.constructor("net.minecraft.world.level.storage.loot.LootContext",
            "net.minecraft.world.level.storage.loot.LootParams", "net.minecraft.util.RandomSource",
            "net.minecraft.core.HolderGetter$Provider");
        var context = (LootContext) Vanilla.create(contextConstructor, params, random, access);

        var amount = tradeSet.calculateNumberOfTrades(context);
        var name = tradeSet.allowDuplicates()
            ? "addOffersFromItemListings"
            : "addOffersFromItemListingsWithoutDuplicates";
        Method draw = AbstractVillager.class.getDeclaredMethod(name, LootContext.class, MerchantOffers.class,
            HolderSet.class, int.class);
        draw.setAccessible(true);
        draw.invoke(null, context, offers, tradeSet.trades(), amount);
    }

    // The same save sections and codecs used by Villager and AbstractVillager. Keep NBT types intact for replay.
    private static void captureSavedVillager(Path outputPath) throws Exception {
        var villager = (Villager) allocate(Villager.class);
        var profession = access.lookupOrThrow(Registries.VILLAGER_PROFESSION)
            .getOrThrow(ResourceKey.create(Registries.VILLAGER_PROFESSION,
                Identifier.withDefaultNamespace("librarian")));
        var type = access.lookupOrThrow(Registries.VILLAGER_TYPE)
            .getOrThrow(ResourceKey.create(Registries.VILLAGER_TYPE, Identifier.withDefaultNamespace("desert")));
        var offers = new MerchantOffers();
        var sequences = new RandomSequences();
        for (var rank = 1; rank <= 5; rank++) {
            setVillagerData(villager, new VillagerData(type, profession, rank));
            addOffers(villager, offers, profession.value().getTrades(rank), sequences, 12345L);
        }

        // Include the wandering trader's component-bearing water-bottle cost as well as the librarian's books.
        var trader = (WanderingTrader) allocate(WanderingTrader.class);
        addOffers(trader, offers, TradeSets.WANDERING_TRADER_BUYING, sequences, 12345L);
        for (var index = 0; index < offers.size(); index++) {
            var offer = offers.get(index);
            for (var use = 0; use < index % (offer.getMaxUses() + 1); use++) {
                offer.increaseUses();
            }
            offer.updateDemand();
            offer.setSpecialPriceDiff(-index);
            var reward = MerchantOffer.class.getDeclaredField("rewardExp");
            reward.setAccessible(true);
            reward.setBoolean(offer, index % 2 == 0);
        }

        var output = net.minecraft.world.level.storage.TagValueOutput.createWithContext(
            net.minecraft.util.ProblemReporter.DISCARDING, access);
        output.store("VillagerData", VillagerData.CODEC, new VillagerData(type, profession, 5));
        output.putInt("Xp", 250);
        output.store("Offers", MerchantOffers.CODEC, offers);
        try (var stream = new java.io.DataOutputStream(Files.newOutputStream(outputPath))) {
            net.minecraft.nbt.NbtIo.write(output.buildResult(), stream);
        }
    }

    private static JsonArray describe(MerchantOffers offers) {
        var rows = new JsonArray();
        for (MerchantOffer offer : offers) {
            rows.add(MerchantOffer.CODEC.encodeStart(ops, offer).getOrThrow());
        }

        return rows;
    }
}
