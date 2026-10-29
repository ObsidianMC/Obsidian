import com.google.common.collect.ImmutableMultimap;
import com.google.gson.*;
import com.mojang.authlib.GameProfile;
import com.mojang.authlib.properties.*;
import io.netty.buffer.*;
import java.nio.file.*;
import java.util.*;
import net.minecraft.SharedConstants;
import net.minecraft.core.*;
import net.minecraft.core.registries.*;
import net.minecraft.nbt.NbtOps;
import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.codec.StreamCodec;
import net.minecraft.network.protocol.common.ClientboundPostEffectsPacket;
import net.minecraft.network.protocol.configuration.*;
import net.minecraft.network.protocol.game.*;
import net.minecraft.network.protocol.login.ClientboundLoginFinishedPacket;
import net.minecraft.resources.*;
import net.minecraft.server.Bootstrap;
import net.minecraft.server.packs.PackType;
import net.minecraft.server.packs.repository.*;
import net.minecraft.server.packs.resources.MultiPackResourceManager;
import net.minecraft.world.Difficulty;
import net.minecraft.world.clock.*;
import net.minecraft.world.entity.player.Abilities;
import net.minecraft.world.level.*;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.chunk.*;
import net.minecraft.world.level.dimension.BuiltinDimensionTypes;
import net.minecraft.world.level.levelgen.Heightmap;
import net.minecraft.world.level.storage.LevelData;

/**
 * Captures what vanilla sends a joining client, from login to its first chunk, by running vanilla's own codecs on
 * synthetic inputs. Obsidian.Tests' JoinPayloads builds the same packets from the same inputs and compares bytes.
 *
 * <p>Also writes {@code synced_registries.json}: the registries vanilla synchronizes, in the order it sends them, with
 * their entries in network id order. Obsidian sends registries it doesn't model from it.
 *
 * <p>Usage: {@code java JoinFixtures.java <fixture.json> <synced_registries.json>}
 */
class JoinFixtures {
    static RegistryAccess access;

    static <T> String hex(StreamCodec<? super RegistryFriendlyByteBuf, T> codec, T value) {
        var bytes = Unpooled.buffer();
        try {
            codec.encode(new RegistryFriendlyByteBuf(bytes, access), value);
            return ByteBufUtil.hexDump(bytes);
        } finally {
            bytes.release();
        }
    }

    // Vanilla's built-in and data pack registries, loaded from the jar's vanilla pack (the core known pack).
    static RegistryAccess loadRegistries() {
        var base = new ArrayList<Registry<?>>();
        BuiltInRegistries.REGISTRY.forEach(base::add);
        var pack = ServerPacksSource.createVanillaPackSource().fullResources();
        var resources = new MultiPackResourceManager(PackType.SERVER_DATA, List.of(pack));
        var loadTags = Vanilla.method("net.minecraft.tags.TagLoader", "loadPendingTags",
            "net.minecraft.server.packs.resources.ResourceManager", "net.minecraft.core.Registry");
        for (var registry : base)
            ((Optional<?>) Vanilla.call(loadTags, null, resources, registry)).ifPresent(tags -> ((Registry.PendingTags<?>) tags).apply());
        var loaded = RegistryDataLoader.load(resources, new ArrayList<HolderLookup.RegistryLookup<?>>(base),
            RegistryDataLoader.WORLD_REGISTRIES, Runnable::run).join();
        var all = new ArrayList<Registry<?>>(base);
        loaded.registries().forEach(entry -> all.add(entry.value()));
        return new RegistryAccess.ImmutableRegistryAccess(all);
    }

    static <T> Holder<T> holder(ResourceKey<? extends Registry<T>> registry, ResourceKey<T> key) {
        return access.lookupOrThrow(registry).getOrThrow(key);
    }

    public static void main(String[] args) throws Exception {
        Locale.setDefault(Locale.US);
        SharedConstants.tryDetectVersion();
        Bootstrap.bootStrap();
        access = loadRegistries();
        var output = new JsonObject();

        var profile = new GameProfile(UUID.fromString("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0"), "Fixture",
            new PropertyMap(ImmutableMultimap.of(
                "textures", new Property("textures", "dGV4dHVyZXM=", "c2lnbmF0dXJl"),
                "cape", new Property("cape", "Y2FwZQ=="))));
        output.addProperty("login_finished", hex(ClientboundLoginFinishedPacket.STREAM_CODEC,
            new ClientboundLoginFinishedPacket(profile, UUID.fromString("01234567-89ab-cdef-fedc-ba9876543210"))));

        var core = KnownPack.vanilla("core");
        output.addProperty("select_known_packs", hex(ClientboundSelectKnownPacks.STREAM_CODEC,
            new ClientboundSelectKnownPacks(List.of(core))));

        // What a client that shares the core pack is sent, in vanilla's order.
        var registryPackets = new JsonArray();
        var synced = new JsonArray();
        RegistrySynchronization.packRegistries(RegistryOps.create(NbtOps.INSTANCE, access), access, Set.of(core), (key, entries) -> {
            if (entries.stream().anyMatch(entry -> entry.data().isPresent()))
                throw new IllegalStateException(key + " has entries outside the core pack");
            registryPackets.add(hex(ClientboundRegistryDataPacket.STREAM_CODEC, new ClientboundRegistryDataPacket(key, entries)));
            var ids = new JsonArray();
            entries.forEach(entry -> ids.add(entry.id().toString()));
            var registry = new JsonObject();
            registry.addProperty("registry", key.identifier().toString());
            registry.add("entries", ids);
            synced.add(registry);
        });
        output.add("registry_data", registryPackets);

        var dimensionTypes = Registries.DIMENSION_TYPE;
        // The seed clients get is the level seed's hash.
        var joinInfo = new CommonPlayerSpawnInfo(holder(dimensionTypes, BuiltinDimensionTypes.OVERWORLD), Level.OVERWORLD,
            net.minecraft.world.level.biome.BiomeManager.obfuscateSeed(1234567890123L), GameType.CREATIVE, Optional.of(GameType.SURVIVAL), false, true,
            Optional.of(GlobalPos.of(Level.NETHER, new BlockPos(-5, 70, 12))), 7, 63);
        var levels = new LinkedHashSet<>(List.of(Level.OVERWORLD, Level.NETHER, Level.END));
        output.addProperty("login", hex(ClientboundLoginPacket.STREAM_CODEC,
            new ClientboundLoginPacket(301, true, levels, 20, 10, 8, false, true, false, joinInfo, true, false)));
        var respawnInfo = new CommonPlayerSpawnInfo(holder(dimensionTypes, BuiltinDimensionTypes.END), Level.END,
            -42L, GameType.SPECTATOR, Optional.empty(), false, false, Optional.empty(), 0, 0);
        output.addProperty("respawn", hex(ClientboundRespawnPacket.STREAM_CODEC, new ClientboundRespawnPacket(respawnInfo, (byte) 3)));

        output.addProperty("change_difficulty", hex(ClientboundChangeDifficultyPacket.STREAM_CODEC,
            new ClientboundChangeDifficultyPacket(Difficulty.HARD, true)));
        output.addProperty("set_default_spawn_position", hex(ClientboundSetDefaultSpawnPositionPacket.STREAM_CODEC,
            new ClientboundSetDefaultSpawnPositionPacket(LevelData.RespawnData.of(Level.OVERWORLD, new BlockPos(8, 64, -24), 90f, -12.5f))));

        var clocks = new LinkedHashMap<Holder<WorldClock>, ClockNetworkState>();
        clocks.put(holder(Registries.WORLD_CLOCK, WorldClocks.OVERWORLD), new ClockNetworkState(30000L, 0f, 1f));
        clocks.put(holder(Registries.WORLD_CLOCK, WorldClocks.THE_END), new ClockNetworkState(777L, 0f, 0f));
        output.addProperty("set_time", hex(ClientboundSetTimePacket.STREAM_CODEC, new ClientboundSetTimePacket(123456789L, clocks)));

        // A raining level's weather, then the chunk wait, as vanilla's PlayerList.sendLevelInfo sends them.
        var events = new JsonArray();
        for (var event : List.of(
            new ClientboundGameEventPacket(ClientboundGameEventPacket.START_RAINING, 0f),
            new ClientboundGameEventPacket(ClientboundGameEventPacket.RAIN_LEVEL_CHANGE, 1f),
            new ClientboundGameEventPacket(ClientboundGameEventPacket.THUNDER_LEVEL_CHANGE, 0.5f),
            new ClientboundGameEventPacket(ClientboundGameEventPacket.LEVEL_CHUNKS_LOAD_START, 0f)))
            events.add(hex(ClientboundGameEventPacket.STREAM_CODEC, event));
        output.add("game_event", events);

        var abilities = new Abilities();
        abilities.invulnerable = true;
        abilities.mayfly = true;
        abilities.instabuild = true;
        abilities.setFlyingSpeed(0.05f);
        abilities.setWalkingSpeed(0.1f);
        output.addProperty("player_abilities", hex(ClientboundPlayerAbilitiesPacket.STREAM_CODEC, new ClientboundPlayerAbilitiesPacket(abilities)));

        output.addProperty("post_effects", hex(ClientboundPostEffectsPacket.STREAM_CODEC, new ClientboundPostEffectsPacket(List.of())));

        chunk(output);

        var json = new GsonBuilder().setPrettyPrinting().create();
        Files.writeString(Path.of(args[0]), json.toJson(output));
        Files.writeString(Path.of(args[1]), json.toJson(synced) + "\n");
    }

    // A chunk with fluids, leaves and a section whose 300 states need the global palette. The placements go in the
    // fixture, so that the test builds the same chunk; the expected bytes are the chunk position, then vanilla's chunk
    // data (heightmaps, sections and block entities) without the light data that follows it.
    static void chunk(JsonObject output) throws Exception {
        var height = new LevelHeightAccessor() {
            public int getHeight() { return 384; }
            public int getMinY() { return -64; }
        };
        var position = new ChunkPos(3, -2);
        var chunk = new ProtoChunk(position, UpgradeData.EMPTY, height, PalettedContainerFactory.create(access), null);
        var placements = new JsonArray();
        var states = Block.BLOCK_STATE_REGISTRY;
        java.util.function.BiConsumer<BlockPos, Integer> place = (pos, state) -> {
            chunk.setBlockState(pos, states.byId(state), 0);
            var row = new JsonArray();
            row.add(pos.getX()); row.add(pos.getY()); row.add(pos.getZ()); row.add(state);
            placements.add(row);
        };
        java.util.function.Function<String, Integer> id = name ->
            states.getId(BuiltInRegistries.BLOCK.getValue(Identifier.parse(name)).defaultBlockState());

        for (int y = 60; y <= 62; y++) place.accept(new BlockPos(0, y, 0), id.apply("minecraft:stone"));
        place.accept(new BlockPos(0, 63, 0), id.apply("minecraft:water"));
        place.accept(new BlockPos(1, 70, 0), id.apply("minecraft:oak_leaves"));
        place.accept(new BlockPos(1, 69, 0), id.apply("minecraft:oak_log"));
        var stairs = BuiltInRegistries.BLOCK.getValue(Identifier.parse("minecraft:oak_stairs")).defaultBlockState()
            .setValue(net.minecraft.world.level.block.state.properties.BlockStateProperties.WATERLOGGED, true);
        place.accept(new BlockPos(2, 10, 0), states.getId(stairs));
        place.accept(new BlockPos(3, -10, 0), id.apply("minecraft:kelp"));
        place.accept(new BlockPos(3, -9, 0), id.apply("minecraft:water"));
        place.accept(new BlockPos(4, 100, 0), id.apply("minecraft:glass"));
        place.accept(new BlockPos(5, -64, 5), id.apply("minecraft:bedrock"));
        // States 1 to 300 in the section from y 16, which takes a 16-bit global palette with 35,723 states.
        for (int state = 1; state <= 300; state++)
            place.accept(new BlockPos(state & 15, 16 + (state >> 8), (state >> 4) & 15), state);

        Heightmap.primeHeightmaps(chunk, EnumSet.of(Heightmap.Types.WORLD_SURFACE, Heightmap.Types.MOTION_BLOCKING,
            Heightmap.Types.MOTION_BLOCKING_NO_LEAVES));
        // Each heightmap's heights by x + z * 16, so that the test gives the chunk vanilla's heights.
        var heightmaps = new LinkedHashMap<Heightmap.Types, long[]>();
        var heights = new JsonObject();
        for (var type : List.of(Heightmap.Types.WORLD_SURFACE, Heightmap.Types.MOTION_BLOCKING, Heightmap.Types.MOTION_BLOCKING_NO_LEAVES)) {
            var heightmap = chunk.getOrCreateHeightmapUnprimed(type);
            heightmaps.put(type, heightmap.getRawData());
            var column = new JsonArray();
            for (int i = 0; i < 256; i++) column.add(heightmap.getFirstAvailable(i & 15, i >> 4));
            heights.add(type.getSerializationKey(), column);
        }

        var sections = Unpooled.buffer();
        var friendly = new RegistryFriendlyByteBuf(sections, access);
        for (var section : chunk.getSections())
            section.write(friendly);
        var buffer = new byte[sections.readableBytes()];
        sections.readBytes(buffer);
        sections.release();

        var constructor = ClientboundLevelChunkPacketData.class.getDeclaredConstructor(Map.class, byte[].class, List.class);
        constructor.setAccessible(true);
        var data = constructor.newInstance(heightmaps, buffer, List.of());

        var bytes = Unpooled.buffer();
        var out = new RegistryFriendlyByteBuf(bytes, access);
        out.writeInt(position.x());
        out.writeInt(position.z());
        ClientboundLevelChunkPacketData.STREAM_CODEC.encode(out, data);
        var chunkFixture = new JsonObject();
        chunkFixture.addProperty("x", position.x());
        chunkFixture.addProperty("z", position.z());
        chunkFixture.add("placements", placements);
        chunkFixture.add("heightmaps", heights);
        chunkFixture.addProperty("data", ByteBufUtil.hexDump(bytes));
        bytes.release();
        output.add("level_chunk", chunkFixture);
    }
}
