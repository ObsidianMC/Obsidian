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
import net.minecraft.network.chat.Component;
import net.minecraft.world.level.block.entity.SignText;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.Holder;
import net.minecraft.core.SectionPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundBlockEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelEventPacket;
import net.minecraft.network.protocol.game.ClientboundOpenScreenPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ClientInformation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.server.players.PlayerList;
import net.minecraft.stats.Stat;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.entity.SignBlockEntity;
import net.minecraft.world.level.block.entity.SignTextSlot;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.gameevent.BlockPositionSource;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.gameevent.GameEventListener;
import net.minecraft.world.level.gameevent.PositionSource;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.Vec3;

/**
 * Records what a player's use of a block does, through the routine the server runs for a use-item-on packet
 * ({@code ServerPlayerGameMode.useItemOn}), on a headless dedicated server in a flat world of air.
 *
 * <p>Each case of the fixture is its input: the blocks set up around the target, the block, face and point clicked,
 * the item in the main hand, the game mode and whether the player sneaks. The probe records the interaction result,
 * every block of the area that changed, the item left in the hand, the entities that appeared, the sounds, level
 * events, block events and game events, the statistics awarded and the menu that opened. When a menu opened, it is
 * closed again and what closing did is recorded under {@code closed}.
 *
 * <p>Sounds, level events and block events are what a second player standing by receives; the acting player's own
 * client predicts the sounds vanilla plays with them as the source, so they aren't sent to it.
 */
public final class BlockUseFixtures {
    // How far from the target the area that is cleared before and compared after each use reaches.
    private static final int Reach = 2;

    // Set without neighbor or shape updates and without drops, like the replay's setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    private static MinecraftServer server;

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(fixturePath)).getAsJsonObject();

        try {
            startServer();
            forceArea(root);
            server.submit(() -> {
                try {
                    var selected = args.length > 1 && !args[1].isBlank()
                        ? Set.of(args[1].split("\\+")) : Set.<String>of();
                    capture(root, selected);
                } catch (Exception e) {
                    throw new RuntimeException(e);
                }
            }).join();
        } catch (Throwable e) {
            e.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }

        root.addProperty("version", server.getServerVersion());
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().serializeNulls().create();
        Files.writeString(fixturePath, compact(gson.toJson(root)) + "\n", StandardCharsets.UTF_8);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();

        // The world is thrown away, so skip the server's save on exit.
        Runtime.getRuntime().halt(0);
    }

    // Forces the chunks around the target and waits until they tick blocks, so pending block events run and new
    // entities can be found, as around a player: the capture itself runs within one task, between ticks.
    private static void forceArea(JsonObject root) throws Exception {
        forceArea(root, server.overworld());
        if (root.getAsJsonArray("cases").asList().stream().anyMatch(element ->
            element.getAsJsonObject().has("dimension"))) {
            forceArea(root, server.getLevel(net.minecraft.world.level.Level.NETHER));
        }
    }

    private static void forceArea(JsonObject root, ServerLevel level) throws Exception {
        var target = position(root.getAsJsonArray("target"));
        server.submit(() -> {
            for (int x = (target.getX() - Reach - 1) >> 4; x <= (target.getX() + Reach + 1) >> 4; x++) {
                for (int z = (target.getZ() - Reach - 1) >> 4; z <= (target.getZ() + Reach + 1) >> 4; z++)
                    level.setChunkForced(x, z, true);
            }
        }).join();

        // The entities placed and dropped are only found once the chunks' entities are loaded and ticking.
        var chunk = net.minecraft.world.level.ChunkPos.containing(target).pack();
        while (!server.submit(() -> ready(level, target, chunk)).join())
            Thread.sleep(50);
    }

    private static boolean ready(ServerLevel level, BlockPos target, long chunk) {
        return level.shouldTickBlocksAt(chunk)
            && level.areEntitiesLoaded(chunk)
            && level.isPositionEntityTicking(target);
    }

    private static void capture(JsonObject root, Set<String> selected) throws Exception {
        captureLevel(root, selected, server.overworld(), "minecraft:overworld");
        if (root.getAsJsonArray("cases").asList().stream().anyMatch(element ->
            element.getAsJsonObject().has("dimension"))) {
            captureLevel(root, selected, server.getLevel(net.minecraft.world.level.Level.NETHER), "minecraft:the_nether");
        }
    }

    private static void captureLevel(JsonObject root, Set<String> selected, ServerLevel level, String dimension)
        throws Exception {
        var target = position(root.getAsJsonArray("target"));
        var standing = root.getAsJsonArray("player");

        for (int x = (target.getX() - Reach - 1) >> 4; x <= (target.getX() + Reach + 1) >> 4; x++) {
            for (int z = (target.getZ() - Reach - 1) >> 4; z <= (target.getZ() + Reach + 1) >> 4; z++)
                level.getChunk(x, z);
        }

        var user = new ProbePlayer(server, level, profile("User"));
        var userPackets = new ArrayList<Packet<?>>();
        user.connection = recorder(userPackets);

        // A second player beside the user hears what is broadcast around it.
        var bystander = new ProbePlayer(server, level, profile("Bystander"));
        var heard = new ArrayList<Packet<?>>();
        bystander.connection = recorder(heard);
        bystander.snapTo(target.getX() + 0.5, target.getY() + 3, target.getZ() + 0.5, 0, 0);
        players(server.getPlayerList()).add(bystander);
        level.players().add(bystander);

        var gameEvents = new ArrayList<String>();
        var listener = new Listener(target, user, gameEvents);
        level.getChunkAt(target).getListenerRegistry(SectionPos.blockToSectionCoord(target.getY())).register(listener);

        for (var element : root.getAsJsonArray("cases")) {
            var input = element.getAsJsonObject();
            var caseDimension = input.has("dimension")
                ? input.get("dimension").getAsString() : "minecraft:overworld";
            if (!caseDimension.equals(dimension))
                continue;

            if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                continue;

            // Clearing the last case's blocks can drop what they held (a jukebox's disc), which goes too.
            clear(level, target);
            discardEntities(level, target);
            for (var setup : input.getAsJsonArray("setup")) {
                var placement = setup.getAsJsonObject();
                var pos = target.offset(position(placement.getAsJsonArray("offset")));
                level.setBlock(pos, state(placement.get("state").getAsString()), QuietFlags);
                if (placement.has("text") && level.getBlockEntity(pos) instanceof SignBlockEntity sign) {
                    var text = new SignText(
                        List.of(Component.literal(placement.get("text").getAsString()),
                            Component.empty(), Component.empty(), Component.empty()),
                        List.of(Component.literal(placement.get("text").getAsString()),
                            Component.empty(), Component.empty(), Component.empty()),
                        net.minecraft.world.item.DyeColor.BLACK, false);
                    sign.setText(text, SignTextSlot.FRONT);
                    sign.setText(text, SignTextSlot.BACK);
                }
                if (placement.has("nbt"))
                    loadData(level, pos, placement.get("nbt").getAsString());
            }

            prepare(user, standing, input);
            if (input.has("cats")) {
                for (var value : input.getAsJsonArray("cats")) {
                    var catInput = value.getAsJsonObject();
                    var catType = BuiltInRegistries.ENTITY_TYPE.getValue(Identifier.parse("minecraft:cat"));
                    var cat = new net.minecraft.world.entity.animal.feline.Cat(
                        (net.minecraft.world.entity.EntityType)catType, level);
                    var at = target.offset(position(catInput.getAsJsonArray("offset")));
                    cat.snapTo(at.getX() + 0.5, at.getY(), at.getZ() + 0.5, 0, 0);
                    cat.setInSittingPose(catInput.get("sitting").getAsBoolean());
                    level.addFreshEntity(cat);
                }
            }

            var before = snapshot(level, target);
            userPackets.clear();
            heard.clear();
            gameEvents.clear();
            user.awarded.clear();
            user.editor = null;

            if (input.has("checkAttack"))
                user.chargeAttack();

            var advancement = prepareCriteria(level, user, input);
            var usedStack = user.getItemInHand(hand(input));

            if (input.has("randomSeed"))
                level.getRandom().setSeed(input.get("randomSeed").getAsLong());

            observeWrites(level, input);
            var result = use(level, user, target, input);
            if (advancement != null && result.consumesAction()) {
                var clicked = target.offset(position(input.getAsJsonArray("clicked")));
                net.minecraft.advancements.triggers.CriteriaTriggers.ANY_BLOCK_USE.trigger(user, clicked, usedStack);
            }
            runBlockEvents(level);
            System.getProperties().remove("obsidian.blockUse.writes");
            System.getProperties().remove("obsidian.blockUse.destroyed");

            if (advancement != null) {
                var progress = user.getAdvancements().getOrStartProgress(advancement);
                var awarded = new JsonArray();
                for (var name : input.getAsJsonObject("criteria").keySet()) {
                    if (progress.getCriterion(name).isDone())
                        awarded.add(name);
                }
                input.add("awardedCriteria", awarded);
            }

            input.addProperty("result", result(result));
            input.addProperty("changes", changes(level, target, before));
            input.addProperty("hand", stack(user.getItemInHand(hand(input))));
            if (input.has("checkInventory")) {
                var inventory = new JsonArray();
                for (int slot = 0; slot < 36; slot++)
                    inventory.add(detailedStack(level, user.getInventory().getItem(slot)));

                input.add("inventory", inventory);
            }
            if (input.has("checkEffects"))
                recordExtra(input, heard);
            if (input.has("checkSign")) {
                var clicked = target.offset(position(input.getAsJsonArray("clicked")));
                var sign = (SignBlockEntity) level.getBlockEntity(clicked);
                var text = sign.getText(sign.getSlotPlayerIsFacing(user));
                input.addProperty("signColor", text.getColor().getName());
                input.addProperty("signGlowing", text.hasGlowingText());
                input.addProperty("signWaxed", sign.isWaxed());
                input.addProperty("editor", user.editor);
            }
            input.add("entities", entities(level, target));
            record(input, heard, gameEvents);
            input.add("stats", strings(user.awarded));
            if (input.has("checkAttack")) {
                user.swingAndResetAttackStrength(hand(input),
                    net.minecraft.world.item.component.SwingAnimation.DEFAULT, true);
                input.addProperty("attackCharge", user.getAttackStrengthScale(0.5f));

                var swingField = net.minecraft.world.entity.LivingEntity.class.getDeclaredField("swingState");
                swingField.setAccessible(true);
                ((net.minecraft.world.entity.LivingEntity.SwingState) swingField.get(user)).tick();
                user.chargeAttack();
                user.swingAndResetAttackStrength(hand(input),
                    net.minecraft.world.item.component.SwingAnimation.DEFAULT, true);
                input.addProperty("blockedAttackCharge", user.getAttackStrengthScale(0.5f));
            }
            input.add("menu", null);

            for (var packet : userPackets) {
                if (packet instanceof ClientboundOpenScreenPacket screen)
                    input.addProperty("menu", BuiltInRegistries.MENU.getKey(screen.getType()).toString());
            }

            if (user.containerMenu != user.inventoryMenu) {
                var afterUse = snapshot(level, target);
                heard.clear();
                gameEvents.clear();
                user.closeContainer();
                runBlockEvents(level);

                var closed = new JsonObject();
                closed.addProperty("changes", changes(level, target, afterUse));
                record(closed, heard, gameEvents);
                input.add("closed", closed);
            }

            if (input.has("checkCloseSources")) {
                use(level, user, target, input);
                use(level, bystander, target, input);
                runBlockEvents(level);
                gameEvents.clear();
                user.closeContainer();
                bystander.closeContainer();
                runBlockEvents(level);
                input.add("explicitCloseEvents", strings(gameEvents));

                use(level, user, target, input);
                runBlockEvents(level);
                gameEvents.clear();
                user.containerMenu = user.inventoryMenu;
                ((net.minecraft.world.level.block.entity.ChestBlockEntity) level.getBlockEntity(target)).recheckOpen();
                runBlockEvents(level);
                input.add("periodicCloseEvents", strings(gameEvents));
            }

            recordHeldItems(level, user, target, input, heard, gameEvents);
        }

        clear(level, target);
        players(server.getPlayerList()).remove(bystander);
        level.players().remove(bystander);
        System.out.println("Used " + root.getAsJsonArray("cases").size() + " blocks");
    }

    // Loads a setup placement's block entity data, SNBT without the id and position, as a chunk load would: quietly,
    // telling no comparator.
    private static void loadData(ServerLevel level, BlockPos pos, String snbt) throws Exception {
        var tag = net.minecraft.nbt.TagParser.parseCompoundFully(snbt);
        var input = net.minecraft.world.level.storage.TagValueInput.create(
            net.minecraft.util.ProblemReporter.DISCARDING, level.registryAccess(), tag);
        level.getBlockEntity(pos).loadWithComponents(input);
    }

    // What the optional checks of items held by blocks record, after everything else: the clicked block entity's
    // saved data, a comparator's output once its pending tick ran, and what breaking the clicked block afterwards does.
    private static void recordHeldItems(ServerLevel level, ProbePlayer user, BlockPos target, JsonObject input,
        List<Packet<?>> heard, List<String> gameEvents) throws Exception {
        var clicked = target.offset(position(input.getAsJsonArray("clicked")));
        if (input.has("checkBlockEntity"))
            input.add("blockEntity", savedData(level, clicked));

        if (input.has("comparator")) {
            var pos = target.offset(position(input.getAsJsonArray("comparator")));
            var state = level.getBlockState(pos);
            if (level.getBlockTicks().hasScheduledTick(pos, state.getBlock()))
                state.tick(level, pos, level.getRandom());

            var comparator = (net.minecraft.world.level.block.entity.ComparatorBlockEntity) level.getBlockEntity(pos);
            input.addProperty("comparatorSignal", comparator.getOutputSignal());
        }

        if (input.has("breakAfterUse")) {
            var before = snapshot(level, target);
            heard.clear();
            gameEvents.clear();
            user.awarded.clear();
            user.gameMode.destroyBlock(clicked);
            runBlockEvents(level);

            var broken = new JsonObject();
            broken.addProperty("changes", changes(level, target, before));
            broken.add("entities", detailedEntities(level, target));
            record(broken, heard, gameEvents);
            broken.add("stats", strings(user.awarded));
            input.add("broken", broken);
        }
    }

    // A block entity's saved data as a chunk saves it, without its position, as vanilla's SNBT; null without one.
    private static com.google.gson.JsonElement savedData(ServerLevel level, BlockPos pos) {
        var blockEntity = level.getBlockEntity(pos);
        if (blockEntity == null)
            return com.google.gson.JsonNull.INSTANCE;

        var saved = blockEntity.saveWithFullMetadata(level.registryAccess());
        saved.remove("x");
        saved.remove("y");
        saved.remove("z");
        return new com.google.gson.JsonPrimitive(saved.toString());
    }

    // Like entities, with each item's components.
    private static JsonArray detailedEntities(ServerLevel level, BlockPos target) {
        var found = new JsonArray();
        for (var entity : level.getEntities((Entity)null, area(target, Reach + 1), entity -> true)) {
            if (entity instanceof ItemEntity item) {
                found.add("item " + detailedStack(level, item.getItem()));
            } else {
                found.add(BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString());
            }
        }

        return found;
    }

    // Like stack, followed by the components the stack changes from its item's as SNBT, when there are any. A
    // lodestone tracker's target is written relative to the target, like every other position the fixture records.
    private static String detailedStack(ServerLevel level, ItemStack stack) {
        var text = stack(stack);
        var patch = stack.getComponentsPatch();
        if (stack.isEmpty() || patch.isEmpty())
            return text;

        var ops = level.registryAccess().createSerializationContext(net.minecraft.nbt.NbtOps.INSTANCE);
        var encoded = (net.minecraft.nbt.CompoundTag) net.minecraft.core.component.DataComponentPatch.CODEC
            .encodeStart(ops, patch)
            .getOrThrow();
        encoded.getCompound("minecraft:lodestone_tracker")
            .flatMap(tracker -> tracker.getCompound("target"))
            .ifPresent(BlockUseFixtures::relativePosition);
        return text + " " + encoded;
    }

    private static void relativePosition(net.minecraft.nbt.CompoundTag target) {
        target.getIntArray("pos").ifPresent(pos -> target.putIntArray("pos", new int[] {
            pos[0] - origin.getX(),
            pos[1] - origin.getY(),
            pos[2] - origin.getZ()
        }));
    }

    private static GameProfile profile(String name) {
        return new GameProfile(UUID.nameUUIDFromBytes(name.getBytes(StandardCharsets.UTF_8)), name);
    }

    private static net.minecraft.advancements.AdvancementHolder prepareCriteria(
        ServerLevel level, ProbePlayer user, JsonObject input) throws Exception {
        if (!input.has("criteria"))
            return null;

        var definition = new JsonObject();
        var criteria = input.getAsJsonObject("criteria").deepCopy();
        // The fixture lists location terms directly; 26.3 encodes that conjunction as an all_of predicate.
        for (var criterion : criteria.entrySet()) {
            var conditions = criterion.getValue().getAsJsonObject().getAsJsonObject("conditions");
            if (!conditions.has("location") || !conditions.get("location").isJsonArray())
                continue;

            var conjunction = new JsonObject();
            conjunction.addProperty("type", "minecraft:all_of");
            for (var term : conditions.getAsJsonArray("location")) {
                var predicate = term.getAsJsonObject();
                predicate.add("type", predicate.remove("condition"));
                if (predicate.get("type").getAsString().equals("minecraft:block_state_property")) {
                    var block = new JsonObject();
                    block.add("blocks", predicate.remove("block"));
                    if (predicate.has("properties"))
                        block.add("state", predicate.remove("properties"));

                    predicate.addProperty("type", "minecraft:match_block");
                    for (var property : block.entrySet())
                        predicate.add(property.getKey(), property.getValue());
                }
            }
            conjunction.add("terms", conditions.get("location"));
            conditions.add("location", conjunction);
        }
        definition.add("criteria", criteria);
        var ops = level.registryAccess().createSerializationContext(com.mojang.serialization.JsonOps.INSTANCE);
        var value = net.minecraft.advancements.Advancement.CODEC.parse(ops, definition).getOrThrow();
        var id = Identifier.parse("probe:" + input.get("name").getAsString());
        var holder = new net.minecraft.advancements.AdvancementHolder(id, value);
        user.getAdvancements().getOrStartProgress(holder);
        var register = net.minecraft.server.PlayerAdvancements.class.getDeclaredMethod(
            "registerListeners", net.minecraft.advancements.AdvancementHolder.class);
        register.setAccessible(true);
        register.invoke(user.getAdvancements(), holder);
        return holder;
    }

    // The scratch classes call these observers only after a changed state is stored, before its side effects.
    private static void observeWrites(ServerLevel level, JsonObject input) throws Exception {
        if (!input.has("checkWrites"))
            return;

        var parameters = new Class<?>[]{Object.class, Object.class, Object.class, int.class};
        net.minecraft.world.level.chunk.LevelChunk.class.getDeclaredMethod("obsidianObserveBlockUse", parameters);
        net.minecraft.world.level.Level.class.getDeclaredMethod("obsidianObserveBlockUse", parameters);
        var writes = new JsonArray();
        var destroyed = new JsonArray();
        input.add("writes", writes);
        input.add("destroyed", destroyed);
        var thread = Thread.currentThread();
        for (var kind : List.of("writes", "destroyed")) {
            java.util.function.Consumer<Object[]> observer = values -> {
                if (values[0] != level || Thread.currentThread() != thread)
                    return;

                var recorded = new JsonObject();
                var pos = (BlockPos)values[1];
                var relative = pos.subtract(origin);
                var offset = new JsonArray();
                offset.add(relative.getX());
                offset.add(relative.getY());
                offset.add(relative.getZ());
                recorded.add("offset", offset);
                recorded.addProperty("state", text((BlockState)values[2]));
                if (kind.equals("writes")) {
                    recorded.addProperty("flags", (Integer)values[3]);
                    writes.add(recorded);
                } else {
                    recorded.addProperty("drops", (Integer)values[3] != 0);
                    destroyed.add(recorded);
                }
            };
            System.getProperties().put("obsidian.blockUse." + kind, observer);
        }
    }

    // The case's game mode, sneaking and held item, with the user standing where the fixture says.
    private static void prepare(ProbePlayer user, JsonArray standing, JsonObject input) {
        user.getInventory().clearContent();
        var mode = input.has("gameMode") ? input.get("gameMode").getAsString() : "survival";
        user.setGameMode(GameType.byName(mode));
        if (input.has("player"))
            standing = input.getAsJsonArray("player");

        user.snapTo(
            standing.get(0).getAsDouble(),
            standing.get(1).getAsDouble(),
            standing.get(2).getAsDouble(),
            standing.get(3).getAsFloat(),
            standing.get(4).getAsFloat());
        user.setYHeadRot(user.getYRot());
        user.setShiftKeyDown(input.has("sneaking") && input.get("sneaking").getAsBoolean());
        user.setItemInHand(InteractionHand.MAIN_HAND, item(input, "item", "count"));
        user.setItemInHand(InteractionHand.OFF_HAND, item(input, "offhand", "offhandCount"));
        if (input.has("fullInventory") && input.get("fullInventory").getAsBoolean()) {
            for (int slot = 1; slot < 36; slot++)
                user.getInventory().setItem(slot, new ItemStack(net.minecraft.world.item.Items.STONE, 64));
        }
    }

    private static ItemStack item(JsonObject input, String name, String countName) {
        if (!input.has(name) || input.get(name).isJsonNull())
            return ItemStack.EMPTY;

        var item = BuiltInRegistries.ITEM.getValue(Identifier.parse(input.get(name).getAsString()));
        var count = input.has(countName) ? input.get(countName).getAsInt() : 2;
        return new ItemStack(item, count);
    }

    // Uses the main hand's item on the case's clicked face, as the server does for a use-item-on packet.
    private static InteractionResult use(ServerLevel level, ProbePlayer user, BlockPos target, JsonObject input) {
        var clicked = target.offset(position(input.getAsJsonArray("clicked")));
        var cursor = input.has("cursor") ? input.getAsJsonArray("cursor") : null;
        var location = new Vec3(
            clicked.getX() + (cursor == null ? 0.5 : cursor.get(0).getAsDouble()),
            clicked.getY() + (cursor == null ? 0.5 : cursor.get(1).getAsDouble()),
            clicked.getZ() + (cursor == null ? 0.5 : cursor.get(2).getAsDouble()));
        var face = Direction.valueOf(input.get("face").getAsString().toUpperCase(Locale.ROOT));
        var hit = new BlockHitResult(location, face, clicked, false);
        var hand = hand(input);
        var stack = user.getItemInHand(hand);

        var result = user.gameMode.useItemOn(user, level, stack, hand, hit);
        if (input.has("rayUse")) {
            input.addProperty("useOnResult", result(result));
            if (!result.consumesAction() && !(result instanceof InteractionResult.Fail))
                return user.gameMode.useItem(user, level, user.getItemInHand(hand), hand);
        }

        return result;
    }

    // Extra observables are opt-in, keeping older case output byte-for-byte stable.
    private static void recordExtra(JsonObject output, List<Packet<?>> heard) {
        var particles = new JsonArray();
        var sounds = new JsonArray();
        for (var packet : heard) {
            if (packet instanceof net.minecraft.network.protocol.game.ClientboundLevelParticlesPacket particle) {
                var type = BuiltInRegistries.PARTICLE_TYPE.getKey(particle.particle().getType());
                particles.add(type + " " + particle.count());
            } else if (packet instanceof ClientboundSoundPacket sound) {
                var at = new BlockPos((int)Math.floor(sound.getX()), (int)Math.floor(sound.getY()),
                    (int)Math.floor(sound.getZ()));
                sounds.add(sound.getSound().value().location() + " " + offset(at) + " " + sound.getVolume());
            }
        }

        output.add("particles", particles);
        output.add("soundDetails", sounds);
    }

    private static InteractionHand hand(JsonObject input) {
        return input.has("handUsed") && input.get("handUsed").getAsString().equals("offhand")
            ? InteractionHand.OFF_HAND : InteractionHand.MAIN_HAND;
    }

    // The result's kind as the packet handler reads it: whether it consumed the action and how the arm swings.
    private static String result(InteractionResult result) {
        if (result instanceof InteractionResult.Success success) {
            return switch (success.swingSource()) {
                case PREDICTED -> "success";
                case SERVER_ONLY -> "success_server";
                case NONE -> "consume";
            };
        }

        if (result instanceof InteractionResult.Fail)
            return "fail";

        if (result instanceof InteractionResult.TryEmptyHandInteraction)
            return "try_with_empty_hand";

        return "pass";
    }

    // The sounds, level events and block events the bystander received, and the game events, in order.
    private static void record(JsonObject output, List<Packet<?>> heard, List<String> gameEvents) {
        var sounds = new ArrayList<String>();
        var levelEvents = new ArrayList<String>();
        var blockEvents = new ArrayList<String>();
        for (var packet : heard) {
            if (packet instanceof ClientboundSoundPacket sound) {
                sounds.add(sound.getSound().value().location().toString());
            } else if (packet instanceof ClientboundLevelEventPacket event) {
                levelEvents.add(event.getType() + " " + offset(event.getPos()) + " " + event.getData());
            } else if (packet instanceof ClientboundBlockEventPacket event) {
                var block = BuiltInRegistries.BLOCK.getKey(event.getBlock()).toString();
                blockEvents.add(block + " " + event.getB0() + " " + event.getB1() + " " + offset(event.getPos()));
            }
        }

        output.add("sounds", strings(sounds));
        output.add("levelEvents", strings(levelEvents));
        output.add("blockEvents", strings(blockEvents));
        output.add("gameEvents", strings(new ArrayList<>(gameEvents)));
    }

    // Positions in events are written relative to the target, which the listener and recorder share.
    private static BlockPos origin;

    private static String offset(BlockPos pos) {
        var relative = pos.subtract(origin);
        return relative.getX() + "," + relative.getY() + "," + relative.getZ();
    }

    // What appeared in the area: items as "item <id> x<count>", and other entities by type.
    private static JsonArray entities(ServerLevel level, BlockPos target) {
        var found = new JsonArray();
        for (var entity : level.getEntities((Entity)null, area(target, Reach + 1), entity -> true)) {
            if (entity instanceof ItemEntity item) {
                found.add("item " + stack(item.getItem()));
            } else {
                found.add(BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString());
            }
        }

        return found;
    }

    private static void discardEntities(ServerLevel level, BlockPos target) {
        for (var entity : level.getEntities((Entity)null, area(target, Reach + 1), entity -> true))
            entity.discard();
    }

    private static AABB area(BlockPos target, int reach) {
        return new AABB(target).inflate(reach);
    }

    // An item stack as "<id> x<count>", with its damage when it has any, or "empty".
    private static String stack(ItemStack stack) {
        if (stack.isEmpty())
            return "empty";

        var text = BuiltInRegistries.ITEM.getKey(stack.getItem()) + " x" + stack.getCount();
        return stack.getDamageValue() > 0 ? text + " damage=" + stack.getDamageValue() : text;
    }

    private static void runBlockEvents(ServerLevel level) throws Exception {
        var method = ServerLevel.class.getDeclaredMethod("runBlockEvents");
        method.setAccessible(true);
        method.invoke(level);
    }

    @SuppressWarnings("unchecked")
    private static List<ServerPlayer> players(PlayerList list) throws Exception {
        var field = PlayerList.class.getDeclaredField("players");
        field.setAccessible(true);
        return (List<ServerPlayer>)field.get(list);
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

    private static void clear(ServerLevel level, BlockPos target) {
        origin = target;
        var air = Blocks.AIR.defaultBlockState();
        for (var pos : BlockPos.betweenClosed(target.offset(-Reach - 1, -Reach - 1, -Reach - 1),
            target.offset(Reach + 1, Reach + 1, Reach + 1))) {
            level.setBlock(pos, air, QuietFlags);
        }
    }

    private static List<BlockState> snapshot(ServerLevel level, BlockPos target) {
        var states = new ArrayList<BlockState>();
        for (var pos : compared(target))
            states.add(level.getBlockState(pos));

        return states;
    }

    private static String changes(ServerLevel level, BlockPos target, List<BlockState> before) {
        var changed = new ArrayList<String>();
        int index = 0;
        for (var pos : compared(target)) {
            var state = level.getBlockState(pos);
            if (state != before.get(index)) {
                var offset = pos.subtract(target);
                changed.add(offset.getX() + "," + offset.getY() + "," + offset.getZ() + "=" + text(state));
            }

            index++;
        }

        return String.join(";", changed);
    }

    // The compared area in a fixed order: y, then z, then x ascending.
    private static List<BlockPos> compared(BlockPos target) {
        var positions = new ArrayList<BlockPos>();
        for (int y = -Reach; y <= Reach; y++) {
            for (int z = -Reach; z <= Reach; z++) {
                for (int x = -Reach; x <= Reach; x++)
                    positions.add(target.offset(x, y, z));
            }
        }

        return positions;
    }

    // The state's block and the properties it doesn't share with the block's default state.
    private static String text(BlockState state) {
        var name = BuiltInRegistries.BLOCK.getKey(state.getBlock()).toString();
        var defaults = state.getBlock().defaultBlockState();
        var properties = new ArrayList<String>();
        for (Property<?> property : state.getProperties()) {
            if (!state.getValue(property).equals(defaults.getValue(property)))
                properties.add(property.getName() + "=" + value(state, property));
        }

        return properties.isEmpty() ? name : name + "[" + String.join(",", properties) + "]";
    }

    private static <T extends Comparable<T>> String value(BlockState state, Property<T> property) {
        return property.getName(state.getValue(property));
    }

    private static BlockState state(String text) {
        var bracket = text.indexOf('[');
        var name = bracket < 0 ? text : text.substring(0, bracket);
        var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse(name));
        var state = block.defaultBlockState();
        if (bracket >= 0) {
            for (var pair : text.substring(bracket + 1, text.length() - 1).split(",")) {
                var parts = pair.split("=");
                state = withValue(state, block.getStateDefinition().getProperty(parts[0]), parts[1]);
            }
        }

        return state;
    }

    private static <T extends Comparable<T>> BlockState withValue(BlockState state, Property<T> property,
        String value) {
        return state.setValue(property, property.getValue(value).orElseThrow());
    }

    private static BlockPos position(JsonArray values) {
        return new BlockPos(values.get(0).getAsInt(), values.get(1).getAsInt(), values.get(2).getAsInt());
    }

    private static JsonArray strings(List<String> values) {
        var array = new JsonArray();
        for (var value : values)
            array.add(value);

        return array;
    }

    // Keeps each short array of numbers on one line.
    private static String compact(String json) {
        var numbers = java.util.regex.Pattern.compile("\\[\\s*(-?[\\d.]+(?:,\\s*-?[\\d.]+)*)\\s*\\]");
        var matcher = numbers.matcher(json.replace("\r\n", "\n"));
        var result = new StringBuilder();
        while (matcher.find()) {
            var inner = matcher.group(1).replaceAll("\\s+", "").replace(",", ", ");
            matcher.appendReplacement(result, java.util.regex.Matcher.quoteReplacement("[" + inner + "]"));
        }

        matcher.appendTail(result);
        return result.toString();
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

    /** A player whose statistics are recorded, and who has no sign editor to open. */
    private static final class ProbePlayer extends ServerPlayer {
        final List<String> awarded = new ArrayList<>();
        String editor;

        ProbePlayer(MinecraftServer server, ServerLevel level, GameProfile profile) {
            super(server, level, profile, ClientInformation.createDefault());
        }

        void chargeAttack() {
            this.attackStrengthTicker = 30;
        }

        @Override public void openTextEdit(SignBlockEntity sign, SignTextSlot slot) {
            editor = slot.toString().toLowerCase(Locale.ROOT);
        }

        @Override public void awardStat(Stat<?> stat, int amount) {
            awarded.add(statName(stat) + " " + amount);
            super.awardStat(stat, amount);
        }

        private static <T> String statName(Stat<T> stat) {
            var type = BuiltInRegistries.STAT_TYPE.getKey(stat.getType());
            var value = stat.getType().getRegistry().getKey(stat.getValue());
            return type + " " + value;
        }
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
