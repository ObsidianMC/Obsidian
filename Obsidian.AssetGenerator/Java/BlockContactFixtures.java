import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import io.netty.channel.ChannelFutureListener;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import java.util.Set;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.SectionPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundBlockEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelEventPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.network.protocol.game.ClientboundLevelParticlesPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ClientInformation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.server.players.PlayerList;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.gameevent.BlockPositionSource;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.gameevent.GameEventListener;
import net.minecraft.world.level.gameevent.PositionSource;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/**
 * Records controlled native entity moves and their block effects on a real dedicated server. Each tick supplies
 * a displacement/velocity, as a movement driver does; Entity.move owns collision, fall accounting and restitution,
 * and applyEffectsFromBlocks owns stepped-on/inside dispatch. AI, travel acceleration and natural random ticks
 * are deliberately outside this contact boundary. All observations start at checkpoint zero, before the first move.
 */
public final class BlockContactFixtures {
    private static MinecraftServer server;
    private static BlockPos origin;

    public static void main(String[] args) throws Exception {
        var path = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(path)).getAsJsonObject();
        var selected = args.length > 1 && !args[1].isBlank()
            ? Set.of(args[1].split("\\+")) : Set.<String>of();
        try {
            startServer();
            var level = server.overworld();
            server.submit(() -> {
                for (int x = -1; x <= 1; x++) {
                    for (int z = -1; z <= 1; z++) {
                        level.setChunkForced(x, z, true);
                    }
                }
            }).join();
            while (!server.submit(() -> level.isPositionEntityTicking(new BlockPos(0, 64, 0))).join()) {
                Thread.sleep(50);
            }
            server.submit(() -> {
                try {
                    for (var value : root.getAsJsonArray("scenarios")) {
                        var input = value.getAsJsonObject();
                        if (!input.has("contactMoves")) {
                            continue;
                        }
                        if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString())) {
                            continue;
                        }
                        capture(level, input);
                    }
                } catch (Exception exception) {
                    throw new RuntimeException(exception);
                }
            }).join();
        } catch (Throwable exception) {
            exception.printStackTrace();
            Runtime.getRuntime().halt(1);
        }
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().serializeNulls().create();
        Files.writeString(path, gson.toJson(root) + "\n", StandardCharsets.UTF_8);
        System.out.flush();
        Runtime.getRuntime().halt(0);
    }

    private static void capture(ServerLevel level, JsonObject input) throws Exception {
        System.out.println("Capturing native contact moves " + input.get("name").getAsString());
        origin = new BlockPos(0, 64, 0);
        var min = position(input.getAsJsonArray("min"));
        var max = position(input.getAsJsonArray("max"));
        var area = new AABB(Vec3.atLowerCornerOf(min), Vec3.atLowerCornerOf(max).add(1, 1, 1));
        level.getBlockTicks().clearArea(new net.minecraft.world.level.levelgen.structure.BoundingBox(
            min.getX(), min.getY(), min.getZ(), max.getX(), max.getY(), max.getZ()));
        for (var entity : level.getEntities((Entity)null, area.inflate(4), entity -> true)) {
            entity.discard();
        }
        for (var pos : BlockPos.betweenClosed(min, max)) {
            level.setBlock(pos, Blocks.AIR.defaultBlockState(), 818);
        }
        for (var value : input.getAsJsonArray("setup")) {
            var placement = value.getAsJsonObject();
            level.setBlock(position(placement.getAsJsonArray("pos")), state(placement.get("state").getAsString()), 3);
        }
        server.getCommands().performPrefixedCommand(server.createCommandSourceStack(),
            "gamerule minecraft:mob_griefing " + (!input.has("mobGriefing")
                || input.get("mobGriefing").getAsBoolean()));
        server.getCommands().performPrefixedCommand(server.createCommandSourceStack(),
            "gamerule minecraft:block_drops " + (!input.has("blockDrops")
                || input.get("blockDrops").getAsBoolean()));
        var raining = input.has("raining") && input.get("raining").getAsBoolean();
        level.getWeatherData().setRaining(raining);
        level.setRainLevel(raining ? 1.0F : 0.0F);
        level.getRandom().setSeed(input.get("seed").getAsLong());
        var watcher = new ServerPlayer(server, level, new GameProfile(UUID.randomUUID(), "ContactWatcher"),
            ClientInformation.createDefault());
        var packets = new ArrayList<Packet<?>>();
        watcher.connection = recorder(packets);
        watcher.snapTo(0.5, 70, 0.5, 0, 0);
        players(server.getPlayerList()).add(watcher);
        level.players().add(watcher);
        var gameEvents = new ArrayList<String>();
        var listener = new Listener(origin, null, gameEvents);
        var registry = level.getChunkAt(origin).getListenerRegistry(SectionPos.blockToSectionCoord(origin.getY()));
        registry.register(listener);
        var entities = new java.util.LinkedHashMap<String, Entity>();
        var checkpoints = new JsonArray();
        input.add("checkpoints", checkpoints);
        var writes = new JsonArray();
        var entityEvents = new JsonArray();
        java.util.function.Consumer<Object[]> eventObserver = values -> {
            if (values[0] == level) {
                var entity = (Entity)values[1];
                var tag = entities.entrySet().stream().filter(entry -> entry.getValue() == entity)
                    .map(Map.Entry::getKey).findFirst().orElse("other");
                entityEvents.add(tag + " " + values[3]);
            }
        };
        System.getProperties().put("obsidian.blockUse.entityEvents", eventObserver);
        java.util.function.Consumer<Object[]> observer = values -> {
            if (values[0] != level) {
                return;
            }
            var write = new JsonObject();
            write.add("pos", vector((BlockPos)values[1]));
            write.addProperty("state", text((BlockState)values[2]));
            write.addProperty("flags", (Integer)values[3]);
            writes.add(write);
        };
        System.getProperties().put("obsidian.blockUse.writes", observer);
        for (int tick = 0; tick <= input.get("ticks").getAsInt(); tick++) {
            packets.clear();
            gameEvents.clear();
            writes.asList().clear();
            entityEvents.asList().clear();
            if (tick > 0 && input.has("scheduledTicks")) {
                var data = (net.minecraft.world.level.storage.ServerLevelData)level.getLevelData();
                data.setGameTime(level.getGameTime() + 1);
                level.getBlockTicks().tick(level.getGameTime(), 65536, (pos, block) -> {
                    var current = level.getBlockState(pos);
                    if (current.is(block)) {
                        current.tick(level, pos, level.getRandom());
                    }
                });
            }
            for (var value : input.getAsJsonArray("entities")) {
                var event = value.getAsJsonObject();
                if (event.get("tick").getAsInt() == tick) {
                    event(level, event, entities);
                }
            }
            if (tick > 0 && input.has("tickFire")) {
                for (var entity : entities.values()) {
                    if (!entity.isRemoved()) {
                        entity.baseTick();
                    }
                }
            }
            // Pose refreshes must be observed without Entity.move refreshing the box for them.
            if (tick > 0 && !input.has("stationaryPlayer")) {
                for (var entity : entities.values()) {
                    if (entity.isRemoved()) {
                        continue;
                    }
                    var before = entity.position();
                    entity.move(net.minecraft.world.entity.MoverType.SELF, entity.getDeltaMovement());
                    // A non-experimental minecart already runs its inside effects from move.
                    if (!(entity instanceof net.minecraft.world.entity.vehicle.minecart.AbstractMinecart)) {
                        entity.applyEffectsFromBlocks(before, entity.position());
                    }
                }
            }
            var checkpoint = checkpoint(level, min, max, tick, entities);
            if (input.has("recordInside")) {
                recordInside(level, checkpoint, input, entities);
                checkpoint.add("entityEvents", entityEvents.deepCopy());
            }
            if (input.has("stationaryPlayer")) {
                recordPlayerState(checkpoint, entities);
            }
            if (input.has("recordSafeFallDistance")) {
                for (var entry : entities.entrySet()) {
                    var living = (net.minecraft.world.entity.LivingEntity)entry.getValue();
                    checkpoint.getAsJsonObject("observations").getAsJsonObject(entry.getKey()).addProperty(
                        "safeFallDistance", living.getAttributeValue(
                            net.minecraft.world.entity.ai.attributes.Attributes.SAFE_FALL_DISTANCE));
                }
            }
            checkpoint.add("writes", writes.deepCopy());
            record(checkpoint, packets, gameEvents);
            checkpoints.add(checkpoint);
        }
        input.addProperty("nextRandom", level.getRandom().nextInt());
        if (input.has("recordInside")) {
            var entityRandoms = new JsonObject();
            for (var entry : entities.entrySet()) {
                entityRandoms.addProperty(entry.getKey(), entry.getValue().getRandom().nextInt());
            }
            input.add("nextEntityRandom", entityRandoms);
        }
        System.getProperties().remove("obsidian.blockUse.writes");
        System.getProperties().remove("obsidian.blockUse.entityEvents");
        registry.unregister(listener);
        level.players().remove(watcher);
        players(server.getPlayerList()).remove(watcher);
        for (var entity : entities.values()) {
            if (entity instanceof ServerPlayer player) {
                level.removePlayerImmediately(player, Entity.RemovalReason.DISCARDED);
            } else {
                entity.discard();
            }
        }
    }

    private static void event(ServerLevel level, JsonObject event, Map<String, Entity> entities) throws Exception {
        if (event.has("summon")) {
            var typeName = event.get("type").getAsString();
            var type = BuiltInRegistries.ENTITY_TYPE.getValue(Identifier.parse(typeName));
            var entity = typeName.equals("minecraft:player")
                ? player(level)
                : type.create(level, net.minecraft.world.entity.EntitySpawnReason.COMMAND);
            var pos = vec(event.getAsJsonArray("pos"));
            entity.snapTo(pos.x, pos.y, pos.z, 0, 0);
            if (event.has("pose")) {
                entity.setPose(net.minecraft.world.entity.Pose.valueOf(event.get("pose").getAsString()));
            }
            entity.setNoGravity(!event.has("noGravity") || event.get("noGravity").getAsBoolean());
            entity.setShiftKeyDown(event.has("sneaking") && event.get("sneaking").getAsBoolean());
            if (event.has("entitySeed")) {
                entity.getRandom().setSeed(event.get("entitySeed").getAsLong());
            }
            if (event.has("fireTicks")) {
                entity.setRemainingFireTicks(event.get("fireTicks").getAsInt());
            }
            if (event.has("silent")) {
                entity.setSilent(event.get("silent").getAsBoolean());
            }
            if (entity instanceof ServerPlayer player && event.has("gameMode")) {
                player.setGameMode(net.minecraft.world.level.GameType.byName(event.get("gameMode").getAsString()));
                player.noPhysics = player.isSpectator();
            }
            if (entity instanceof net.minecraft.world.entity.Mob mob) {
                mob.setNoAi(true);
            }
            if (entity instanceof ItemEntity item) {
                item.setItem(new ItemStack(BuiltInRegistries.ITEM.getValue(
                    Identifier.parse(event.get("item").getAsString())),
                    event.has("count") ? event.get("count").getAsInt() : 1));
                item.setNeverPickUp();
            }
            if (entity instanceof net.minecraft.world.entity.LivingEntity living) {
                living.getRandom().setSeed(event.get("entitySeed").getAsLong());
                if (event.has("health")) {
                    living.setHealth(event.get("health").getAsFloat());
                }
                if (event.has("baby") && entity instanceof net.minecraft.world.entity.AgeableMob ageable) {
                    ageable.setAge(-24000);
                }
                if (event.has("jumpBoost")) {
                    living.addEffect(new net.minecraft.world.effect.MobEffectInstance(
                        net.minecraft.world.effect.MobEffects.JUMP_BOOST, 1000, event.get("jumpBoost").getAsInt()));
                }
                if (event.has("feet")) {
                    living.setItemSlot(net.minecraft.world.entity.EquipmentSlot.FEET, new ItemStack(
                        BuiltInRegistries.ITEM.getValue(Identifier.parse(event.get("feet").getAsString()))));
                }
                if (event.has("equipment")) {
                    for (var entry : event.getAsJsonObject("equipment").entrySet()) {
                        var slot = net.minecraft.world.entity.EquipmentSlot.byName(entry.getKey());
                        living.setItemSlot(slot, new ItemStack(BuiltInRegistries.ITEM.getValue(
                            Identifier.parse(entry.getValue().getAsString()))));
                    }
                }
                if (event.has("fireResistance")) {
                    living.addEffect(new net.minecraft.world.effect.MobEffectInstance(
                        net.minecraft.world.effect.MobEffects.FIRE_RESISTANCE, 1000));
                }
                if (event.has("frostWalker")) {
                    var boots = new ItemStack(net.minecraft.world.item.Items.DIAMOND_BOOTS);
                    var enchantment = level.registryAccess().lookupOrThrow(
                        net.minecraft.core.registries.Registries.ENCHANTMENT).getOrThrow(
                            net.minecraft.world.item.enchantment.Enchantments.FROST_WALKER);
                    boots.enchant(enchantment, 1);
                    var slot = event.has("frostWalkerSlot")
                        ? net.minecraft.world.entity.EquipmentSlot.HEAD
                        : net.minecraft.world.entity.EquipmentSlot.FEET;
                    living.setItemSlot(slot, boots);
                }
            }
            entity.setDeltaMovement(vec(event.getAsJsonArray("motion")));
            if (event.has("fallDistance")) {
                entity.fallDistance = event.get("fallDistance").getAsDouble();
            }
            if (entity instanceof ServerPlayer player) {
                level.addNewPlayer(player);
            } else {
                level.addFreshEntity(entity);
            }
            entities.put(event.get("summon").getAsString(), entity);
        } else if (event.has("remove")) {
            var entity = entities.get(event.get("remove").getAsString());
            if (entity instanceof ServerPlayer player) {
                level.removePlayerImmediately(player, Entity.RemovalReason.DISCARDED);
            } else {
                entity.discard();
            }
        } else if (event.has("setBlock")) {
            level.setBlock(position(event.getAsJsonArray("setBlock")), state(event.get("state").getAsString()), 3);
        } else if (event.has("move")) {
            entities.get(event.get("move").getAsString()).setPos(vec(event.getAsJsonArray("pos")));
        } else if (event.has("updatePose")) {
            var player = (ServerPlayer)entities.get(event.get("updatePose").getAsString());
            player.setShiftKeyDown(event.get("sneaking").getAsBoolean());
            var update = net.minecraft.world.entity.player.Player.class.getDeclaredMethod("updatePlayerPose");
            update.setAccessible(true);
            update.invoke(player);
            if (event.has("tickAir")) {
                player.baseTick();
            }
        } else if (event.has("removeJumpBoost")) {
            var living = (net.minecraft.world.entity.LivingEntity)entities.get(
                event.get("removeJumpBoost").getAsString());
            living.removeEffect(net.minecraft.world.effect.MobEffects.JUMP_BOOST);
        } else if (event.has("jump")) {
            ((net.minecraft.world.entity.LivingEntity)entities.get(event.get("jump").getAsString())).jumpFromGround();
        } else if (event.has("velocity")) {
            entities.get(event.get("velocity").getAsString()).setDeltaMovement(vec(event.getAsJsonArray("motion")));
        } else {
            throw new IllegalArgumentException("Unknown contact event " + event);
        }
    }

    private static void recordInside(ServerLevel level, JsonObject checkpoint, JsonObject input,
        Map<String, Entity> entities) {
        for (var entry : entities.entrySet()) {
            var entity = entry.getValue();
            var observation = checkpoint.getAsJsonObject("observations").getAsJsonObject(entry.getKey());
            observation.addProperty("removed", entity.isRemoved());
            observation.addProperty("fireTicks", entity.getRemainingFireTicks());
            if (input.has("recordFireResistance")
                && entity instanceof net.minecraft.world.entity.LivingEntity living) {
                observation.addProperty("fireResistance", living.hasEffect(
                    net.minecraft.world.effect.MobEffects.FIRE_RESISTANCE));
            }
            if (input.has("tickFire")) {
                try {
                    var flag = Entity.class.getDeclaredMethod("getSharedFlag", int.class);
                    flag.setAccessible(true);
                    observation.addProperty("fireFlag", (Boolean)flag.invoke(entity, 0));
                } catch (ReflectiveOperationException exception) {
                    throw new RuntimeException(exception);
                }
            }
            if (input.has("recordFreeze")) {
                observation.addProperty("frozenTicks", entity.getTicksFrozen());
            }
            if (entity instanceof net.minecraft.world.entity.vehicle.VehicleEntity vehicle) {
                observation.addProperty("vehicleDamage", vehicle.getDamage());
            }
            if (entity instanceof ItemEntity item) {
                observation.addProperty("itemCount", item.getItem().getCount());
            }
        }
        var containers = new JsonArray();
        for (var value : input.getAsJsonArray("setup")) {
            var pos = position(value.getAsJsonObject().getAsJsonArray("pos"));
            if (level.getBlockEntity(pos) instanceof net.minecraft.world.level.block.entity.HopperBlockEntity hopper) {
                var container = new JsonObject();
                container.add("pos", vector(pos));
                var items = new JsonArray();
                for (int slot = 0; slot < hopper.getContainerSize(); slot++) {
                    var stack = hopper.getItem(slot);
                    items.add(stack.isEmpty() ? "air" : BuiltInRegistries.ITEM.getKey(stack.getItem())
                        + " x" + stack.getCount());
                }
                container.add("items", items);
                containers.add(container);
            }
        }
        checkpoint.add("containers", containers);
    }

    private static void recordPlayerState(JsonObject checkpoint, Map<String, Entity> entities) {
        for (var entry : entities.entrySet()) {
            var player = (ServerPlayer)entry.getValue();
            var observation = checkpoint.getAsJsonObject("observations").getAsJsonObject(entry.getKey());
            var box = player.getBoundingBox();
            observation.addProperty("pose", player.getPose().name());
            observation.add("boxMin", vector(new Vec3(box.minX, box.minY, box.minZ)));
            observation.add("boxMax", vector(new Vec3(box.maxX, box.maxY, box.maxZ)));
            observation.add("eyePosition", vector(player.getEyePosition()));
            observation.addProperty("swimming", player.isSwimming());
            observation.addProperty("air", player.getAirSupply());
        }
    }

    // A player without a client, whose packets are kept like the watcher's, that plates and tripwires find.
    private static ServerPlayer player(ServerLevel level) throws Exception {
        var player = new ServerPlayer(server, level, new GameProfile(UUID.randomUUID(), "ContactPlayer"),
            ClientInformation.createDefault());
        var connection = recorder(new ArrayList<>());
        var chunkSender = ServerGamePacketListenerImpl.class.getDeclaredField("chunkSender");
        chunkSender.setAccessible(true);
        chunkSender.set(connection, new net.minecraft.server.network.PlayerChunkSender(false));
        player.connection = connection;
        return player;
    }

    private static JsonObject checkpoint(ServerLevel level, BlockPos min, BlockPos max, int tick,
        Map<String, Entity> entities) {
        var output = new JsonObject();
        output.addProperty("tick", tick);
        var blocks = new JsonArray();
        for (var pos : BlockPos.betweenClosed(min, max)) {
            var block = new JsonObject();
            block.add("pos", vector(pos));
            block.addProperty("state", text(level.getBlockState(pos)));
            blocks.add(block);
        }
        output.add("blocks", blocks);
        var observations = new JsonObject();
        for (var entry : entities.entrySet()) {
            var entity = entry.getValue();
            var observation = new JsonObject();
            observation.add("position", vector(entity.position()));
            observation.add("velocity", vector(entity.getDeltaMovement()));
            observation.addProperty("fallDistance", entity.fallDistance);
            observation.addProperty("onGround", entity.onGround());
            if (entity instanceof net.minecraft.world.entity.LivingEntity living) {
                observation.addProperty("health", living.getHealth());
                var damage = living.getLastDamageSource();
                if (damage == null) {
                    observation.add("damageType", com.google.gson.JsonNull.INSTANCE);
                } else {
                    observation.addProperty("damageType", damage.typeHolder().unwrapKey().orElseThrow()
                        .identifier().toString());
                }
            }
            observations.add(entry.getKey(), observation);
        }
        output.add("observations", observations);
        var drops = new JsonArray();
        var area = new AABB(Vec3.atLowerCornerOf(min), Vec3.atLowerCornerOf(max).add(1, 1, 1));
        for (var entity : level.getEntities((Entity)null, area.inflate(2), entity -> true)) {
            if (entity instanceof ItemEntity item && !entities.containsValue(entity)) {
                drops.add(BuiltInRegistries.ITEM.getKey(item.getItem().getItem()) + " x" + item.getItem().getCount());
            }
        }
        output.add("drops", drops);
        return output;
    }

    private static BlockPos position(JsonArray array) {
        return new BlockPos(array.get(0).getAsInt(), array.get(1).getAsInt(), array.get(2).getAsInt());
    }

    private static Vec3 vec(JsonArray array) {
        return new Vec3(array.get(0).getAsDouble(), array.get(1).getAsDouble(), array.get(2).getAsDouble());
    }

    private static JsonArray vector(BlockPos pos) {
        var result = new JsonArray();
        result.add(pos.getX());
        result.add(pos.getY());
        result.add(pos.getZ());
        return result;
    }

    private static JsonArray vector(Vec3 pos) {
        var result = new JsonArray();
        result.add(pos.x);
        result.add(pos.y);
        result.add(pos.z);
        return result;
    }

    private static BlockState state(String text) {
        var bracket = text.indexOf('[');
        var id = bracket < 0 ? text : text.substring(0, bracket);
        var state = BuiltInRegistries.BLOCK.getValue(Identifier.parse(id)).defaultBlockState();
        if (bracket >= 0) {
            for (var pair : text.substring(bracket + 1, text.length() - 1).split(",")) {
                var parts = pair.split("=");
                var property = state.getBlock().getStateDefinition().getProperty(parts[0]);
                state = property(state, property, parts[1]);
            }
        }
        return state;
    }

    @SuppressWarnings({"rawtypes", "unchecked"})
    private static BlockState property(BlockState state, Property property, String value) {
        return state.setValue(property, (Comparable)property.getValue(value).orElseThrow());
    }

    private static String text(BlockState state) {
        var properties = state.getProperties().stream()
            .sorted(java.util.Comparator.comparing(property -> property.getName()))
            .map(property -> property.getName() + "=" + valueName(state, property)).toList();
        var id = BuiltInRegistries.BLOCK.getKey(state.getBlock()).toString();
        return properties.isEmpty() ? id : id + "[" + String.join(",", properties) + "]";
    }

    private static <T extends Comparable<T>> String valueName(BlockState state, Property<T> property) {
        return property.getName(state.getValue(property));
    }

    @SuppressWarnings("unchecked")
    private static List<ServerPlayer> players(PlayerList list) throws Exception {
        var field = PlayerList.class.getDeclaredField("players");
        field.setAccessible(true);
        return (List<ServerPlayer>)field.get(list);
    }

    private static JsonArray strings(List<String> values) {
        var array = new JsonArray();
        values.forEach(array::add);
        return array;
    }

    private static String offset(BlockPos pos) {
        var relative = pos.subtract(origin);
        return relative.getX() + "," + relative.getY() + "," + relative.getZ();
    }
    // The sounds, level events and block events the bystander received, and the game events, in order.
    private static void record(JsonObject output, List<Packet<?>> heard, List<String> gameEvents) {
        var sounds = new ArrayList<String>();
        var details = new JsonArray();
        var levelEvents = new ArrayList<String>();
        var blockEvents = new ArrayList<String>();
        var particles = new JsonArray();
        for (var packet : heard) {
            if (packet instanceof ClientboundSoundPacket sound) {
                var id = sound.getSound().value().location().toString();
                sounds.add(id);
                var detail = new JsonObject();
                detail.addProperty("id", id);
                detail.addProperty("category", sound.getSource().getName());
                detail.addProperty("volume", sound.getVolume());
                detail.addProperty("pitch", sound.getPitch());
                detail.add("position", vector(new Vec3(sound.getX(), sound.getY(), sound.getZ())));
                details.add(detail);
            } else if (packet instanceof ClientboundLevelParticlesPacket particle) {
                var detail = new JsonObject();
                detail.addProperty("id", BuiltInRegistries.PARTICLE_TYPE.getKey(particle.particle().getType()).toString());
                detail.add("position", vector(new Vec3(particle.x(), particle.y(), particle.z())));
                detail.add("offset", vector(new Vec3(particle.xDist(), particle.yDist(), particle.zDist())));
                detail.add("speed", vector(new Vec3(particle.xMaxSpeed(), particle.yMaxSpeed(), particle.zMaxSpeed())));
                detail.addProperty("count", particle.count());
                detail.addProperty("randomization", particle.randomizationType().ordinal());
                detail.addProperty("overrideLimiter", particle.overrideLimiter());
                detail.addProperty("alwaysShow", particle.alwaysShow());
                particles.add(detail);
            } else if (packet instanceof ClientboundLevelEventPacket event) {
                levelEvents.add(event.getType() + " " + offset(event.getPos()) + " " + event.getData());
            } else if (packet instanceof ClientboundBlockEventPacket event) {
                var block = BuiltInRegistries.BLOCK.getKey(event.getBlock()).toString();
                blockEvents.add(block + " " + event.getB0() + " " + event.getB1() + " " + offset(event.getPos()));
            }
        }

        output.add("sounds", strings(sounds));
        output.add("soundDetails", details);
        output.add("levelEvents", strings(levelEvents));
        output.add("blockEvents", strings(blockEvents));
        output.add("gameEvents", strings(new ArrayList<>(gameEvents)));
        output.add("particles", particles);
    }

    // A game connection that keeps the packets sent to it instead of sending them.
    private static ServerGamePacketListenerImpl recorder(List<Packet<?>> packets) throws Exception {
        var unsafeField = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = unsafeField.get(null);
        var allocate = unsafe.getClass().getMethod("allocateInstance", Class.class);
        var recorder = (Recorder)allocate.invoke(unsafe, Recorder.class);
        recorder.packets = packets;
        return recorder;
    }

    private static void startServer() throws Exception {
        Files.writeString(Path.of("eula.txt"), "eula=true\n");

        // A flat world of air in plains, with nothing spawning and no watchdog, since the whole capture runs as
        // one task on the server thread.
        var properties = String.join("\n",
            "level-type=minecraft\\:flat",
            "generator-settings={\"layers\"\\:[{\"block\"\\:\"minecraft\\:air\",\"height\"\\:1}],"
                + "\"biome\"\\:\"minecraft\\:plains\"}",
            "generate-structures=false",
            "spawn-protection=0",
            "gamemode=survival",
            "online-mode=false",
            "server-port=0",
            "max-tick-time=-1",
            "pause-when-empty-seconds=-1",
            "sync-chunk-writes=false",
            "view-distance=2",
            "simulation-distance=2",
            "enable-jmx-monitoring=false",
            "enable-rcon=false",
            "enable-query=false",
            "");
        Files.writeString(Path.of("server.properties"), properties);

        net.minecraft.server.Main.main(new String[]{"--nogui"});

        // Main keeps the server only in its shutdown hook, so find it there.
        var hooksField = Class.forName("java.lang.ApplicationShutdownHooks").getDeclaredField("hooks");
        hooksField.setAccessible(true);
        @SuppressWarnings("unchecked")
        var hooks = (Map<Thread, Thread>) hooksField.get(null);

        for (var hook : new ArrayList<>(hooks.keySet())) {
            if (hook.getClass().getName().equals("net.minecraft.server.Main$1")) {
                var field = hook.getClass().getDeclaredField("val$dedicatedServer");
                field.setAccessible(true);
                server = (MinecraftServer) field.get(hook);
            }
        }

        if (server == null)
            throw new IllegalStateException("The dedicated server didn't start.");

        while (!server.isReady())
            Thread.sleep(50);
    }

    /** A connection allocated without its constructor, which keeps what is sent to it. */
    private static final class Recorder extends ServerGamePacketListenerImpl {
        List<Packet<?>> packets;

        private Recorder() {
            super(null, null, null, null);
            throw new UnsupportedOperationException();
        }

        @Override public void send(Packet<?> packet) {
            packets.add(packet);
        }

        @Override public void send(Packet<?> packet, ChannelFutureListener listener) {
            packets.add(packet);
        }
    }

    /** Records every game event within reach of the target, by name, offset and source. */
    private static final class Listener implements GameEventListener {
        private final BlockPos target;
        private final Entity user;
        private final List<String> events;

        Listener(BlockPos target, Entity user, List<String> events) {
            this.target = target;
            this.user = user;
            this.events = events;
        }

        @Override public PositionSource getListenerSource() {
            return new BlockPositionSource(target);
        }

        @Override public int getListenerRadius() {
            return 16;
        }

        @Override public boolean handleGameEvent(ServerLevel level, Holder<GameEvent> event, GameEvent.Context context,
            Vec3 pos) {
            var name = event.unwrapKey().orElseThrow().identifier().toString();
            var block = BlockPos.containing(pos).subtract(target);
            var source = source(context.sourceEntity());
            events.add(name + " " + block.getX() + "," + block.getY() + "," + block.getZ() + " " + source);
            return true;
        }

        // The event's source: the user as "player", another entity by type, or "none".
        private String source(Entity entity) {
            if (entity == null)
                return "none";

            if (entity == user)
                return "player";

            return BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString();
        }
    }
}
