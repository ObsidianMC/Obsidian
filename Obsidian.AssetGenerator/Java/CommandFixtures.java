import com.google.gson.*;
import com.mojang.brigadier.tree.*;
import io.netty.buffer.Unpooled;
import java.nio.file.*;
import java.util.*;
import net.minecraft.commands.*;
import net.minecraft.commands.synchronization.*;
import net.minecraft.commands.synchronization.SuggestionProviders;
import net.minecraft.core.*;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.FriendlyByteBuf;
import net.minecraft.server.dedicated.DedicatedServerProperties;
import net.minecraft.world.item.*;
import net.minecraft.world.item.slot.*;
import net.minecraft.world.level.storage.loot.*;
import net.minecraft.world.level.storage.loot.parameters.LootContextParams;
import net.minecraft.util.context.ContextMap;
import net.minecraft.util.RandomSource;
import net.minecraft.world.SimpleContainer;
import net.minecraft.commands.arguments.SlotSourceArgument;
import net.minecraft.server.commands.item.ItemCommands;
import java.lang.reflect.*;

/** Captures the 26.3 command grammar, including wire argument properties and suggestion providers. */
@SuppressWarnings("deprecation")
public final class CommandFixtures {
    private static JsonArray markerCompletions;
    private static JsonArray postEffects() throws Exception {
        var player = (net.minecraft.server.level.ServerPlayer)allocate(net.minecraft.server.level.ServerPlayer.class);
        field(net.minecraft.world.entity.player.Player.class, player, "postEffects", new ArrayList<>());
        var source = Commands.createCompilationContext(net.minecraft.server.permissions.PermissionSet.ALL_PERMISSIONS)
            .withSuppressedOutput();
        var rows = new JsonArray();
        for (var operation : List.of("clear", "add", "add", "list", "remove", "remove", "add", "clear")) {
            var row = new JsonObject();
            row.addProperty("operation", operation);
            var name = switch (operation) {
                case "add" -> "addPostEffect";
                case "remove" -> "removePostEffect";
                case "clear" -> "clearPostEffect";
                default -> "listPostEffects";
            };
            var command = net.minecraft.server.commands.PostEffectCommand.class;
            Class<?>[] types;
            if (operation.equals("list")) {
                types = new Class<?>[]{CommandSourceStack.class, net.minecraft.server.level.ServerPlayer.class};
            } else if (operation.equals("clear")) {
                types = new Class<?>[]{CommandSourceStack.class, Collection.class};
            } else {
                types = new Class<?>[]{CommandSourceStack.class, Collection.class, net.minecraft.resources.Identifier.class};
            }
            var method = command.getDeclaredMethod(name, types);
            method.setAccessible(true);
            try {
                Object result;
                if (operation.equals("list")) {
                    result = method.invoke(null, source, player);
                } else if (operation.equals("clear")) {
                    result = method.invoke(null, source, List.of(player));
                } else {
                    var effect = net.minecraft.resources.Identifier.parse("test:effect");
                    result = method.invoke(null, source, List.of(player), effect);
                }
                row.addProperty("success", true);
                row.addProperty("result", (int)result);
            } catch (InvocationTargetException exception) {
                if (!(exception.getCause() instanceof com.mojang.brigadier.exceptions.CommandSyntaxException)) {
                    throw exception;
                }
                row.addProperty("success", false);
                row.addProperty("result", 0);
            }
            var effects = new JsonArray();
            player.getPostEffects().forEach(effect -> effects.add(effect.toString()));
            row.add("effects", effects);
            rows.add(row);
        }
        return rows;
    }

    private static JsonArray spam() {
        var rows = new JsonArray();
        for (int threshold : new int[]{-1, 0, 1, 10}) {
            var chat = new net.minecraft.util.TickThrottler(20, threshold * 20);
            var command = new net.minecraft.util.TickThrottler(20, threshold * 20);
            var events = new JsonArray();
            for (int event = 0; event < 26; event++) {
                var operation = switch (event % 3) {
                    case 0 -> "chat";
                    case 1 -> "command";
                    default -> "tick";
                };
                if (operation.equals("chat")) {
                    chat.increment();
                } else if (operation.equals("command")) {
                    command.increment();
                } else {
                    chat.tick();
                    command.tick();
                }
                var state = new JsonObject();
                state.addProperty("operation", operation);
                state.addProperty("chat", chat.isUnderThreshold());
                state.addProperty("command", command.isUnderThreshold());
                events.add(state);
            }
            var row = new JsonObject();
            row.addProperty("threshold", threshold);
            row.add("events", events);
            rows.add(row);
        }
        return rows;
    }

    private static JsonArray data() throws Exception {
        var rows = new JsonArray();
        for (var operation : List.of("set", "append", "prepend", "insert")) {
            for (var pathText : List.of("value", "values", "missing")) {
                var root = net.minecraft.nbt.TagParser.parseCompoundFully("{value:7,values:[1,2]}");
                var path = net.minecraft.commands.arguments.NbtPathArgument.NbtPath.of(pathText);
                var row = new JsonObject();
                row.addProperty("operation", operation);
                row.addProperty("path", pathText);
                try {
                    var number = net.minecraft.nbt.IntTag.valueOf(7);
                    var index = operation.equals("append") ? -1 : 0;
                    var changed = operation.equals("set") ? path.set(root, number)
                        : path.insert(index, root, List.of(number));
                    row.addProperty("success", changed != 0);
                    row.addProperty("result", changed);
                } catch (com.mojang.brigadier.exceptions.CommandSyntaxException exception) {
                    row.addProperty("success", false);
                    row.addProperty("result", 0);
                }
                var after = net.minecraft.nbt.NbtOps.INSTANCE.convertTo(com.mojang.serialization.JsonOps.INSTANCE, root);
                row.add("after", after);
                rows.add(row);
            }
        }
        return rows;
    }
    private static final class ClockServer extends net.minecraft.server.dedicated.DedicatedServer {
        static net.minecraft.server.dedicated.DedicatedPlayerList players;
        static net.minecraft.world.level.gamerules.GameRules rules;

        private ClockServer() {
            super(null, null, null, null, Optional.empty(), null, null, null, null, null);
        }

        @Override public net.minecraft.server.dedicated.DedicatedPlayerList getPlayerList() { return players; }
        @Override public Iterable<net.minecraft.server.level.ServerLevel> getAllLevels() { return List.of(); }
        @Override public net.minecraft.world.level.gamerules.GameRules getGlobalGameRules() { return rules; }
    }

    private static Object allocate(Class<?> type) throws Exception {
        var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        field.setAccessible(true);
        var unsafe = field.get(null);
        return unsafe.getClass().getMethod("allocateInstance", Class.class).invoke(unsafe, type);
    }

    private static void field(Class<?> owner, Object target, String name, Object value) throws Exception {
        var field = owner.getDeclaredField(name);
        field.setAccessible(true);
        field.set(target, value);
    }

    @SuppressWarnings({"unchecked", "deprecation"})
    private static JsonArray time(Commands commands, HolderLookup.Provider access) throws Exception {
        var server = (ClockServer)allocate(ClockServer.class);
        var manager = (net.minecraft.world.clock.ServerClockManager)Vanilla.create(Vanilla.constructor(
            "net.minecraft.world.clock.ServerClockManager", "net.minecraft.world.clock.PackedClockStates"),
            net.minecraft.world.clock.PackedClockStates.EMPTY);
        field(net.minecraft.server.MinecraftServer.class, server, "clockManager", manager);
        field(net.minecraft.world.clock.ServerClockManager.class, manager, "server", server);
        var playerListType = net.minecraft.server.dedicated.DedicatedPlayerList.class;
        ClockServer.players = (net.minecraft.server.dedicated.DedicatedPlayerList)allocate(playerListType);
        field(net.minecraft.server.players.PlayerList.class, ClockServer.players, "players", List.of());
        ClockServer.rules = new net.minecraft.world.level.gamerules.GameRules(
            List.of(net.minecraft.world.level.gamerules.GameRules.ADVANCE_TIME));
        var level = (net.minecraft.server.level.ServerLevel)allocate(net.minecraft.server.level.ServerLevel.class);
        var levelDataType = net.minecraft.world.level.storage.WritableLevelData.class;
        var levelData = Proxy.newProxyInstance(levelDataType.getClassLoader(), new Class<?>[]{levelDataType},
            (proxy, method, values) -> method.getName().equals("getGameTime") ? 123456L : null);
        field(net.minecraft.world.level.Level.class, level, "levelData", levelData);
        field(net.minecraft.server.MinecraftServer.class, server, "levels",
            Map.of(net.minecraft.world.level.Level.OVERWORLD, level));
        var clockField = net.minecraft.world.clock.ServerClockManager.class.getDeclaredField("clocks");
        clockField.setAccessible(true);
        var clocks = (Map<Holder<net.minecraft.world.clock.WorldClock>,
            net.minecraft.world.clock.ServerClockManager.ServerClockInstance>)clockField.get(manager);
        var clockInstanceType = net.minecraft.world.clock.ServerClockManager.ServerClockInstance.class;
        var markerField = clockInstanceType.getDeclaredField("timeMarkers");
        markerField.setAccessible(true);
        var registeredClocks = access.lookupOrThrow(net.minecraft.core.registries.Registries.WORLD_CLOCK);
        var registeredTimelines = access.lookupOrThrow(net.minecraft.core.registries.Registries.TIMELINE);
        for (var clock : registeredClocks.listElements().toList()) {
            var instance = new net.minecraft.world.clock.ServerClockManager.ServerClockInstance();
            clocks.put(clock, instance);
            var markers = (Map<Object, Object>)markerField.get(instance);
            for (var timeline : registeredTimelines.listElements().toList()) {
                timeline.value().registerTimeMarkers((key, marker) -> {
                    if (marker.clock().equals(clock)) {
                        markers.put(key, marker);
                    }
                });
            }
        }
        var source = new CommandSourceStack(CommandSource.NULL, net.minecraft.world.phys.Vec3.ZERO,
            net.minecraft.world.phys.Vec2.ZERO, level, net.minecraft.server.permissions.PermissionSet.ALL_PERMISSIONS,
            net.minecraft.network.chat.Component.literal("fixture"), server).withSuppressedOutput();
        markerCompletions = new JsonArray();
        var markerMethod = net.minecraft.server.commands.TimeCommand.class.getDeclaredMethod("suggestTimeMarkers",
            CommandSourceStack.class, com.mojang.brigadier.suggestion.SuggestionsBuilder.class, Holder.class);
        markerMethod.setAccessible(true);
        for (var entry : clocks.keySet()) {
            var suggestions = (java.util.concurrent.CompletableFuture<com.mojang.brigadier.suggestion.Suggestions>)
                markerMethod.invoke(null, source, new com.mojang.brigadier.suggestion.SuggestionsBuilder("", 0), entry);
            var row = new JsonObject();
            row.addProperty("clock", entry.unwrapKey().orElseThrow().identifier().toString());
            var values = new JsonArray();
            suggestions.join().getList().forEach(value -> values.add(value.getText()));
            row.add("suggestions", values);
            markerCompletions.add(row);
        }
        var rows = new JsonArray();
        for (var suffix : List.of("query time", "query minecraft:day", "query minecraft:day repetition",
            "set 48001", "add -1s", "add 0t", "pause", "resume", "rate 1", "rate 0.5",
            "set minecraft:day", "query minecraft:moon", "query minecraft:early_game repetition",
            "set 8388609t", "set 2147483647t", "add -2147483648t")) {
            for (var paused : List.of(false, true)) {
                for (var entry : clocks.values()) {
                    entry.loadFrom(new net.minecraft.world.clock.ClockState(48001, 0.75f, 1, paused));
                }
                var command = "time of minecraft:overworld " + suffix;
                var row = new JsonObject();
                row.addProperty("command", command);
                row.addProperty("paused", paused);
                try {
                    row.addProperty("result", commands.getDispatcher().execute(command, source));
                    row.addProperty("success", true);
                } catch (com.mojang.brigadier.exceptions.CommandSyntaxException exception) {
                    row.addProperty("result", 0);
                    row.addProperty("success", false);
                }
                var instance = clocks.values().iterator().next();
                var overworld = access.lookupOrThrow(net.minecraft.core.registries.Registries.WORLD_CLOCK)
                    .getOrThrow(net.minecraft.resources.ResourceKey.create(
                        net.minecraft.core.registries.Registries.WORLD_CLOCK,
                        net.minecraft.resources.Identifier.parse("minecraft:overworld")));
                instance = clocks.get(overworld);
                row.addProperty("ticks", instance.totalTicks());
                row.addProperty("partial", instance.partialTick());
                row.addProperty("rate", instance.rate());
                row.addProperty("afterPaused", instance.isPaused());
                rows.add(row);
            }
        }
        return rows;
    }

    private static LootContext loot(HolderLookup.Provider access, Map<?, ?> parameters) {
        var map = Vanilla.create(Vanilla.constructor("net.minecraft.util.context.ContextMap", "java.util.Map"), parameters);
        var params = Vanilla.create(Vanilla.constructor("net.minecraft.world.level.storage.loot.LootParams",
            "net.minecraft.server.level.ServerLevel", "net.minecraft.util.context.ContextMap", "java.util.Map", "float"),
            null, map, Map.of(), 0f);
        return (LootContext)Vanilla.create(Vanilla.constructor("net.minecraft.world.level.storage.loot.LootContext",
            "net.minecraft.world.level.storage.loot.LootParams", "net.minecraft.util.RandomSource",
            "net.minecraft.core.HolderGetter$Provider"), params, RandomSource.create(1), access);
    }

    private static JsonArray compute(Commands commands, HolderLookup.Provider access) throws Exception {
        var rows = new JsonArray();
        var source = Commands.createCompilationContext(net.minecraft.server.permissions.PermissionSet.ALL_PERMISSIONS)
            .withSuppressedOutput();
        var decoratorType = Class.forName("net.minecraft.server.commands.LootContextSources$ContextDecorator");
        var loot = loot(access, Map.of());
        var decorator = Proxy.newProxyInstance(decoratorType.getClassLoader(), new Class<?>[]{decoratorType},
            (proxy, method, values) -> loot);
        for (var input : List.of(
            "compute default integer {type:'minecraft:constant',value:0}",
            "compute default integer {type:'minecraft:constant',value:-7}",
            "compute default integer {type:'minecraft:add',inputs:[2,3,4]}",
            "compute default integer {type:'minecraft:div',left:1,right:0}",
            "compute default integer {type:'minecraft:add',inputs:[2147483647,1]}",
            "compute default float {type:'minecraft:constant',value:-1.25}",
            "compute default float {type:'minecraft:constant',value:1.75} 2",
            "compute default float {type:'minecraft:div',left:1,right:0}",
            "compute default integer {type:'minecraft:from_float',input:3.75}")) {
            var row = new JsonObject();
            row.addProperty("command", input);
            var parsed = commands.getDispatcher().parse(input, source);
            try {
                Commands.validateParseResults(parsed);
            } catch (com.mojang.brigadier.exceptions.CommandSyntaxException exception) {
                throw new IllegalStateException(input + ": " + exception.getMessage(), exception);
            }
            var context = parsed.getContext().build(input);
            var floating = input.contains("default float ");
            var method = floating
                ? net.minecraft.server.commands.ComputeCommand.class.getDeclaredMethod("computeAsFloat",
                    com.mojang.brigadier.context.CommandContext.class, decoratorType, Holder.class, float.class)
                : net.minecraft.server.commands.ComputeCommand.class.getDeclaredMethod("computeAsInt",
                    com.mojang.brigadier.context.CommandContext.class, decoratorType, Holder.class);
            method.setAccessible(true);
            try {
                var provider = context.getArgument("provider", Holder.class);
                var scale = input.endsWith(" 2") ? 2f : 1f;
                var result = floating ? method.invoke(null, context, decorator, provider, scale)
                    : method.invoke(null, context, decorator, provider);
                row.addProperty("success", true);
                row.addProperty("result", (int)result);
            } catch (InvocationTargetException exception) {
                var cause = exception.getCause();
                if (!(cause instanceof com.mojang.brigadier.exceptions.CommandSyntaxException)) {
                    throw exception;
                }
                row.addProperty("success", false);
                row.addProperty("result", 0);
                row.addProperty("error", cause.getMessage());
            }
            rows.add(row);
        }
        return rows;
    }

    /** Four slots of planks, with slot 0 holding the item whose contents an expression reaches into, if any. */
    private static SimpleContainer slotContainer(String holder) {
        var container = new SimpleContainer(4);
        for (int slot = 0; slot < 4; slot++) {
            container.setItem(slot, new ItemStack(Items.OAK_PLANKS, 3));
        }
        switch (holder) {
            case "chest" -> {
                var chest = new ItemStack(Items.CHEST);
                chest.set(net.minecraft.core.component.DataComponents.CONTAINER,
                    net.minecraft.world.item.component.ItemContainerContents.fromItems(
                        List.of(new ItemStack(Items.OAK_PLANKS, 3), new ItemStack(Items.OAK_PLANKS, 4))));
                container.setItem(0, chest);
            }
            case "bundle" -> container.setItem(0, new ItemStack(Items.BUNDLE));
            case "dirt_bundle" -> {
                var bundle = new ItemStack(Items.BUNDLE);
                var contents = new net.minecraft.world.item.component.BundleContents.Mutable();
                contents.tryInsert(new ItemStack(Items.DIRT, 60));
                bundle.set(net.minecraft.core.component.DataComponents.BUNDLE_CONTENTS, contents.toImmutable());
                container.setItem(0, bundle);
            }
            case "crossbow" -> container.setItem(0, new ItemStack(Items.CROSSBOW));
            default -> { }
        }
        return container;
    }

    private static JsonObject stack(ItemStack stack) {
        var row = new JsonObject();
        row.addProperty("item", BuiltInRegistries.ITEM.getKey(stack.getItem()).toString());
        row.addProperty("count", stack.getCount());
        var components = Map.of("minecraft:container", net.minecraft.core.component.DataComponents.CONTAINER,
            "minecraft:bundle_contents", net.minecraft.core.component.DataComponents.BUNDLE_CONTENTS,
            "minecraft:charged_projectiles", net.minecraft.core.component.DataComponents.CHARGED_PROJECTILES);
        for (var entry : components.entrySet()) {
            var contents = (net.minecraft.world.item.component.ContainerComponent<?>)stack.get(entry.getValue());
            if (contents == null) {
                continue;
            }
            var items = new JsonArray();
            contents.itemCopies().forEach(item -> items.add(stack(item)));
            row.addProperty("component", entry.getKey());
            row.add("contents", items);
        }
        return row;
    }

    private static JsonArray slots(CommandBuildContext context, HolderLookup.Provider access) throws Exception {
        var rows = new JsonArray();
        var setter = ItemCommands.class.getDeclaredMethod("lambda$setItems$0", ItemProvider.class, SlotSelector.class,
            net.minecraft.server.commands.CommandResponseTracker.class, Object.class, SlotCollection.class);
        setter.setAccessible(true);
        var holders = Map.of(
            "{type:'minecraft:contents',component:'minecraft:container',"
                + "slot_source:{type:'minecraft:slot_range',slots:'container.0'}}", List.of("chest"),
            "{type:'minecraft:contents',component:'minecraft:bundle_contents',"
                + "slot_source:{type:'minecraft:slot_range',slots:'container.0'}}", List.of("bundle", "dirt_bundle"),
            "{type:'minecraft:limit_slots',limit:3,slot_source:{type:'minecraft:contents',"
                + "component:'minecraft:charged_projectiles',"
                + "slot_source:{type:'minecraft:slot_range',slots:'container.0'}}}", List.of("crossbow"));
        var expressions = List.of("container.*", "container.0", "{type:'minecraft:empty'}",
            "{type:'minecraft:slot_range',slots:'container.*'}",
            "{type:'minecraft:limit_slots',limit:2,slot_source:{type:'minecraft:slot_range',slots:'container.*'}}",
            "[{type:'minecraft:slot_range',slots:'container.1'},{type:'minecraft:slot_range',slots:'container.0'}]",
            "{type:'minecraft:filtered',item_filter:{items:'minecraft:oak_planks'},"
                + "slot_source:{type:'minecraft:slot_range',slots:'container.*'}}",
            "{type:'minecraft:contents',component:'minecraft:container',"
                + "slot_source:{type:'minecraft:slot_range',slots:'container.0'}}",
            "{type:'minecraft:contents',component:'minecraft:bundle_contents',"
                + "slot_source:{type:'minecraft:slot_range',slots:'container.0'}}",
            "{type:'minecraft:limit_slots',limit:3,slot_source:{type:'minecraft:contents',"
                + "component:'minecraft:charged_projectiles',"
                + "slot_source:{type:'minecraft:slot_range',slots:'container.0'}}}");
        var cases = new ArrayList<String[]>();
        for (var expression : expressions) {
            for (var holder : holders.getOrDefault(expression, List.of("none"))) {
                cases.add(new String[]{expression, holder});
            }
        }
        for (var testCase : cases) {
            var expression = testCase[0];
            var holder = testCase[1];
            for (var operation : List.of("replace", "fill", "override")) {
                var container = slotContainer(holder);
                var other = slotContainer(holder);
                var source = SlotSourceArgument.slotSource(context).parse(new com.mojang.brigadier.StringReader(expression));
                var selected = source.value().provide(loot(access, Map.of(LootContextParams.CONTAINER, container)));
                var second = source.value().provide(loot(access, Map.of(LootContextParams.CONTAINER, other)));
                var items = List.of(new ItemStack(Items.STONE, 2), new ItemStack(Items.DIRT, 1));
                var provider = switch (operation) {
                    case "fill" -> ItemCommands.fillItemProvider(items);
                    case "override" -> ItemCommands.overrideItemProvider(items);
                    default -> ItemCommands.replaceItemProvider(items);
                };
                var tracker = net.minecraft.server.commands.CommandResponseTracker.create();
                var selector = SlotSelector.tracking(new org.apache.commons.lang3.mutable.MutableInt());
                var row = new JsonObject();
                row.addProperty("expression", expression);
                row.addProperty("operation", operation);
                row.addProperty("holder", holder);
                row.addProperty("slots", selected.size());
                int changed = (int)setter.invoke(null, provider, selector, tracker, container, selected);
                int otherChanged = (int)setter.invoke(null, provider, selector, tracker, other, second);
                row.addProperty("changed", changed + otherChanged);
                var afterSlots = source.value().provide(loot(access, Map.of(LootContextParams.CONTAINER, container)));
                row.addProperty("afterSlots", afterSlots.size());
                var after = new JsonArray();
                for (var target : List.of(container, other)) {
                    for (int slot = 0; slot < 4; slot++) {
                        after.add(stack(target.getItem(slot)));
                    }
                }
                row.add("after", after);
                rows.add(row);
            }
        }
        return rows;
    }

    /** Which counts {@code /item ... with <item> <count>} accepts: vanilla validates the created stack. */
    private static JsonArray itemCounts(CommandBuildContext context) throws Exception {
        var rows = new JsonArray();
        for (var input : List.of("stone 64", "stone 65", "ender_pearl 16", "ender_pearl 17", "diamond_sword 1",
            "diamond_sword 2")) {
            var parts = input.split(" ");
            var item = net.minecraft.commands.arguments.item.ItemArgument.item(context)
                .parse(new com.mojang.brigadier.StringReader(parts[0]));
            var count = Integer.parseInt(parts[1]);
            var row = new JsonObject();
            row.addProperty("item", parts[0]);
            row.addProperty("count", count);
            try {
                item.createItemStack(count);
                row.addProperty("success", true);
            } catch (com.mojang.brigadier.exceptions.CommandSyntaxException exception) {
                row.addProperty("success", false);
            }
            rows.add(row);
        }
        return rows;
    }

    /** What {@code /data get} returns for each kind of tag, read from a fixed compound (see the test). */
    private static JsonArray dataGet() throws Exception {
        var data = new net.minecraft.nbt.CompoundTag();
        data.putInt("int", 7);
        data.putDouble("double", -2.5);
        data.putString("string", "hello");
        var list = new net.minecraft.nbt.ListTag();
        for (int value = 1; value <= 3; value++) {
            list.add(net.minecraft.nbt.IntTag.valueOf(value));
        }
        data.put("list", list);
        var compound = new net.minecraft.nbt.CompoundTag();
        compound.putInt("a", 1);
        compound.putInt("b", 2);
        data.put("compound", compound);
        data.putByte("byte", (byte)3);
        data.putIntArray("ints", new int[]{1, 2});
        var accessorType = net.minecraft.server.commands.data.DataAccessor.class;
        var accessor = Proxy.newProxyInstance(accessorType.getClassLoader(), new Class<?>[]{accessorType},
            (proxy, method, values) -> switch (method.getName()) {
                case "getData" -> data.copy();
                case "setData" -> null;
                default -> net.minecraft.network.chat.Component.empty();
            });
        var source = Commands.createCompilationContext(net.minecraft.server.permissions.PermissionSet.ALL_PERMISSIONS)
            .withSuppressedOutput();
        var commands = net.minecraft.server.commands.data.DataCommands.class;
        var whole = commands.getDeclaredMethod("getData", CommandSourceStack.class, accessorType);
        var atPath = commands.getDeclaredMethod("getData", CommandSourceStack.class, accessorType,
            net.minecraft.commands.arguments.NbtPathArgument.NbtPath.class);
        whole.setAccessible(true);
        atPath.setAccessible(true);
        var rows = new JsonArray();
        for (var path : List.of("", "int", "double", "string", "list", "compound", "byte", "ints", "list[0]",
            "missing")) {
            var row = new JsonObject();
            row.addProperty("path", path);
            try {
                Object result;
                if (path.isEmpty()) {
                    result = whole.invoke(null, source, accessor);
                } else {
                    var parsed = net.minecraft.commands.arguments.NbtPathArgument.nbtPath()
                        .parse(new com.mojang.brigadier.StringReader(path));
                    result = atPath.invoke(null, source, accessor, parsed);
                }
                row.addProperty("success", true);
                row.addProperty("result", (int)result);
            } catch (InvocationTargetException exception) {
                if (!(exception.getCause() instanceof com.mojang.brigadier.exceptions.CommandSyntaxException)) {
                    throw exception;
                }
                row.addProperty("success", false);
                row.addProperty("result", 0);
            }
            rows.add(row);
        }
        return rows;
    }

    /** Completions through {@code /execute run}, which redirects to the root, next to the command alone. */
    private static JsonArray executeCompletions(Commands commands) {
        var source = Commands.createCompilationContext(net.minecraft.server.permissions.PermissionSet.ALL_PERMISSIONS);
        var rows = new JsonArray();
        for (var input : List.of("compute ", "execute run compute ", "execute as @s run item ", "execute run comp")) {
            var parsed = commands.getDispatcher().parse(input, source);
            var suggestions = commands.getDispatcher().getCompletionSuggestions(parsed).join();
            var row = new JsonObject();
            row.addProperty("command", input);
            row.addProperty("start", suggestions.getRange().getStart());
            var values = new JsonArray();
            suggestions.getList().forEach(value -> values.add(value.getText()));
            row.add("suggestions", values);
            rows.add(row);
        }
        return rows;
    }

    /** How vanilla's NumericTag converts each number type, which entity loading relies on. */
    private static JsonArray numericTags() {
        var rows = new JsonArray();
        var tags = List.<net.minecraft.nbt.NumericTag>of(net.minecraft.nbt.ByteTag.valueOf((byte)-1),
            net.minecraft.nbt.ShortTag.valueOf((short)300), net.minecraft.nbt.IntTag.valueOf(70000),
            net.minecraft.nbt.LongTag.valueOf(1L << 40), net.minecraft.nbt.FloatTag.valueOf(-2.5f),
            net.minecraft.nbt.FloatTag.valueOf(3.0e9f), net.minecraft.nbt.DoubleTag.valueOf(1.0e20),
            net.minecraft.nbt.DoubleTag.valueOf(Double.NaN), net.minecraft.nbt.DoubleTag.valueOf(65537.75));
        for (var tag : tags) {
            var row = new JsonObject();
            row.addProperty("type", tag.getType().getName());
            row.addProperty("value", tag.toString());
            row.addProperty("byte", tag.byteValue());
            row.addProperty("short", tag.shortValue());
            row.addProperty("int", tag.intValue());
            row.addProperty("long", tag.longValue());
            row.addProperty("float", Float.toString(tag.floatValue()));
            row.addProperty("double", Double.toString(tag.doubleValue()));
            rows.add(row);
        }
        return rows;
    }

    @SuppressWarnings({"rawtypes", "unchecked"})
    private static JsonObject node(CommandNode<CommandSourceStack> node, Commands commands) {
        var output = ArgumentUtils.serializeNodeToJson(commands.getDispatcher(), node);
        // The serializer leaves out a redirect to the root (execute run), whose path is empty; keep it as [].
        if (node.getRedirect() == commands.getDispatcher().getRoot() && !output.has("redirect")) {
            output.add("redirect", new JsonArray());
        }
        var source = Commands.createCompilationContext(net.minecraft.server.permissions.PermissionSet.NO_PERMISSIONS);
        output.addProperty("restricted", !node.getRequirement().test(source));
        if (node instanceof ArgumentCommandNode argument) {
            var type = argument.getType();
            ArgumentTypeInfo info = ArgumentTypeInfos.byClass(type);
            var buffer = new FriendlyByteBuf(Unpooled.buffer());
            try {
                buffer.writeVarInt(BuiltInRegistries.COMMAND_ARGUMENT_TYPE.getId(info));
                info.serializeToNetwork(info.unpack(type), buffer);
                var bytes = new byte[buffer.readableBytes()];
                buffer.readBytes(bytes);
                output.addProperty("wire", HexFormat.of().formatHex(bytes));
            } finally {
                buffer.release();
            }

            if (argument.getCustomSuggestions() != null) {
                output.addProperty("suggestions", SuggestionProviders.getName(argument.getCustomSuggestions()).toString());
            }
        }

        if (!node.getChildren().isEmpty()) {
            var children = new JsonObject();
            for (var child : node.getChildren()) {
                children.add(child.getName(), node(child, commands));
            }
            output.add("children", children);
        }
        return output;
    }

    public static void main(String[] arguments) throws Exception {
        VanillaDumper.bootstrap();
        var access = (HolderLookup.Provider)VanillaDumper.vanillaRegistries();
        access = new RegistrySetBuilder()
            .add(net.minecraft.core.registries.Registries.SLOT_SOURCE, bootstrap -> {})
            .build(access);
        var context = Commands.createValidationContext(access);
        var commands = new Commands(Commands.CommandSelection.ALL, context);
        var output = new JsonObject();
        var tree = new JsonObject();
        var names = List.of("swing", "time", "compute", "posteffect", "item", "execute", "place",
            "publish", "unpublish", "data");
        for (var name : names) {
            tree.add(name, node(commands.getDispatcher().getRoot().getChild(name), commands));
        }
        output.add("tree", tree);

        var ranges = new JsonObject();
        for (var name : net.minecraft.world.inventory.SlotRanges.allNames().toList()) {
            var slots = new JsonArray();
            for (int slot : net.minecraft.world.inventory.SlotRanges.nameToIds(name).slots()) {
                slots.add(slot);
            }
            ranges.add(name, slots);
        }
        output.add("slot_ranges", ranges);
        output.add("compute", compute(commands, access));
        output.add("slots", slots(context, access));
        output.add("item_counts", itemCounts(context));
        output.add("numeric_tags", numericTags());
        output.add("data_get", dataGet());
        output.add("execute_completions", executeCompletions(commands));
        output.add("time", time(commands, access));
        output.add("marker_completions", markerCompletions);
        output.add("posteffect", postEffects());
        output.add("spam", spam());
        output.add("data", data());
        var swing = net.minecraft.world.item.component.SwingAnimation.DEFAULT;
        var animation = new JsonObject();
        animation.addProperty("type", swing.type().getSerializedName());
        animation.addProperty("duration", swing.duration());
        output.add("swing_default", animation);
        var noOp = new net.minecraft.world.level.levelgen.feature.NoOpFeature();
        var featureResult = noOp.place(null, null, RandomSource.create(1), net.minecraft.core.BlockPos.ZERO);
        output.addProperty("no_op_feature", featureResult);

        var defaults = new DedicatedServerProperties(new Properties());
        var settings = new JsonObject();
        settings.addProperty("whitelist", defaults.whiteList.get());
        settings.addProperty("chatSpamThresholdSeconds", defaults.chatSpamThresholdSeconds);
        settings.addProperty("commandSpamThresholdSeconds", defaults.commandSpamThresholdSeconds);
        output.add("defaults", settings);
        Files.writeString(Path.of(arguments[0]), new GsonBuilder().setPrettyPrinting().create().toJson(output));
    }
}
