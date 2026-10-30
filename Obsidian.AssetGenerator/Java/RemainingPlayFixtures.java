import com.google.gson.*;
import com.mojang.serialization.JsonOps;
import java.nio.file.*;
import java.util.*;
import net.minecraft.SharedConstants;
import net.minecraft.advancements.*;
import net.minecraft.core.RegistryAccess;
import net.minecraft.core.BlockPos;
import net.minecraft.core.particles.*;
import net.minecraft.core.registries.*;
import net.minecraft.network.protocol.game.*;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.*;
import net.minecraft.server.Bootstrap;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.util.random.*;
import net.minecraft.world.entity.ai.attributes.*;
import net.minecraft.world.level.gamerules.*;
import net.minecraft.world.phys.Vec3;
import net.minecraft.world.scores.*;

/** Synthetic inputs passed through 26.3's packet codecs and server advancement layout. */
class RemainingPlayFixtures extends ScreenFixtures {
    static void capture(JsonArray rows, Object packet) {
        rows.add(encode(packet.getClass().getName(), "STREAM_CODEC", packet));
    }
    public static void main(String[] args) throws Exception {
        SharedConstants.tryDetectVersion();
        Bootstrap.bootStrap();
        access = RegistryAccess.fromRegistryOfRegistries(BuiltInRegistries.REGISTRY);

        var output = new JsonObject();
        output.add("set_player_team", teams());
        output.add("level_particles", levelParticles());
        output.add("explode", explosions());

        var transientBlocks = new JsonArray();
        var stone = net.minecraft.world.level.block.Blocks.STONE.defaultBlockState();
        capture(transientBlocks, new ClientboundAddTransientBlockPacket(new BlockPos(-12, 70, 23), stone));
        output.add("add_transient_block", transientBlocks);
        var disk = new JsonArray();
        capture(disk, ClientboundLowDiskSpaceWarningPacket.INSTANCE);
        output.add("low_disk_space_warning", disk);

        gameRules(output);
        attributes(output);
        output.add("update_advancements", advancements());
        extraPackets(output);
        recipePackets(output);
        Files.writeString(Path.of(args[0]), new GsonBuilder().setPrettyPrinting().create().toJson(output));
    }

    static JsonArray teams() {
        var teams = new JsonArray();
        var scoreboard = new Scoreboard();
        var team = scoreboard.addPlayerTeam("fixture");
        team.setDisplayName(Component.literal("Team"));
        team.setPlayerPrefix(Component.literal("["));
        team.setPlayerSuffix(Component.literal("]"));
        team.setNameTagVisibility(Team.Visibility.HIDE_FOR_OTHER_TEAMS);
        team.setCollisionRule(Team.CollisionRule.NEVER);
        team.setAllowFriendlyFire(true);
        team.setSeeFriendlyInvisibles(true);
        team.setColor(Optional.of(TeamColor.RED));
        scoreboard.addPlayerToTeam("Alex", team);

        capture(teams, ClientboundSetPlayerTeamPacket.createAddOrModifyPacket(team, true));
        capture(teams, ClientboundSetPlayerTeamPacket.createAddOrModifyPacket(team, false));
        capture(teams, ClientboundSetPlayerTeamPacket.createRemovePacket(team));
        var add = ClientboundSetPlayerTeamPacket.Action.ADD;
        var remove = ClientboundSetPlayerTeamPacket.Action.REMOVE;
        capture(teams, ClientboundSetPlayerTeamPacket.createPlayerPacket(team, "Steve", add));
        capture(teams, ClientboundSetPlayerTeamPacket.createPlayerPacket(team, "Alex", remove));

        team.setColor(Optional.empty());
        team.setNameTagVisibility(Team.Visibility.NEVER);
        team.setCollisionRule(Team.CollisionRule.PUSH_OWN_TEAM);
        capture(teams, ClientboundSetPlayerTeamPacket.createAddOrModifyPacket(team, false));
        return teams;
    }

    static JsonArray levelParticles() {
        var particles = new JsonArray();
        // Every options-free type is exercised, including all types emitted by gameplay and /particle.
        for (var type : BuiltInRegistries.PARTICLE_TYPE) {
            if (!(type instanceof SimpleParticleType simple)) continue;

            var row = new JsonObject();
            row.addProperty("id", BuiltInRegistries.PARTICLE_TYPE.getId(type));
            row.addProperty("name", BuiltInRegistries.PARTICLE_TYPE.getKey(type).toString());
            var wires = new JsonArray();
            for (var randomization : ClientboundLevelParticlesPacket.RandomizationType.values()) {
                capture(wires, new ClientboundLevelParticlesPacket(simple, true, false, -12.25, 64.5, 31.875,
                    .25f, .5f, .75f, 1f, 2f, 3f, 301, randomization));
            }
            row.add("wire", wires);
            particles.add(row);
        }
        return particles;
    }

    static JsonArray explosions() {
        var explosions = new JsonArray();
        var boom = SoundEvent.createFixedRangeEvent(Identifier.parse("fixture:boom"), 24f);
        var sound = net.minecraft.core.Holder.direct(boom);
        var smoke = new ExplosionParticleInfo(ParticleTypes.SMOKE, .5f, .75f);
        var debris = WeightedList.<ExplosionParticleInfo>builder().add(smoke, 3).build();
        capture(explosions, new ClientboundExplodePacket(new Vec3(-12.25, 64.5, 31.875), 4f, 301,
            Optional.of(new Vec3(.25, -.5, 1.5)), ParticleTypes.EXPLOSION_EMITTER, sound, debris, true));
        capture(explosions, new ClientboundExplodePacket(new Vec3(-12.25, 64.5, 31.875), 4f, 0,
            Optional.empty(), ParticleTypes.EXPLOSION_EMITTER, sound, WeightedList.of(), false));
        return explosions;
    }

    static void gameRules(JsonObject output) {
        var rules = new GameRules(BuiltInRegistries.GAME_RULE.stream().toList());
        var values = new LinkedHashMap<ResourceKey<GameRule<?>>, String>();
        var bounds = new JsonObject();
        var parsing = new JsonArray();
        var sorted = BuiltInRegistries.GAME_RULE.stream()
            .sorted(Comparator.comparing(r -> r.getIdentifier().toString()))
            .toList();
        var inputs = List.of("true", "false", "TRUE", "1", "-1", "0", "+1", "2147483647", "2147483648", "1 ", " 1");
        for (var rule : sorted) {
            var key = ResourceKey.create(Registries.GAME_RULE, rule.getIdentifier());
            values.put(key, rules.getAsString(rule));
            if (rule.argument() instanceof com.mojang.brigadier.arguments.IntegerArgumentType integer) {
                var limits = new JsonArray();
                limits.add(integer.getMinimum());
                limits.add(integer.getMaximum());
                bounds.add(rule.id(), limits);
            }
            for (String value : inputs) {
                var row = new JsonArray();
                row.add(rule.getIdentifier().toString());
                row.add(value);
                row.add(rule.deserialize(value).result().isPresent());
                parsing.add(row);
            }
        }

        var ruleWires = new JsonArray();
        capture(ruleWires, new ClientboundGameRuleValuesPacket(values));
        output.add("game_rule_values", ruleWires);
        output.add("game_rule_parsing", parsing);
        output.add("game_rule_bounds", bounds);

        var keepInventory = ResourceKey.create(Registries.GAME_RULE, Identifier.parse("minecraft:keep_inventory"));
        var randomTickSpeed = ResourceKey.create(Registries.GAME_RULE, Identifier.parse("minecraft:random_tick_speed"));
        var edits = new JsonArray();
        capture(edits, new ServerboundSetGameRulePacket(List.of(
            new ServerboundSetGameRulePacket.Entry(keepInventory, "true"),
            new ServerboundSetGameRulePacket.Entry(randomTickSpeed, "17"))));
        output.add("set_game_rule", edits);
    }

    static void attributes(JsonObject output) {
        var attributes = new ArrayList<ClientboundUpdateAttributesPacket.AttributeSnapshot>();
        var ranges = new JsonArray();
        for (var attribute : BuiltInRegistries.ATTRIBUTE) {
            var ranged = (RangedAttribute) attribute;
            var row = new JsonArray();
            row.add(BuiltInRegistries.ATTRIBUTE.getKey(attribute).toString());
            row.add(ranged.getMinValue());
            row.add(ranged.getMaxValue());
            row.add(ranged.isClientSyncable());
            row.add(ranged.getDefaultValue());
            ranges.add(row);

            var modifiers = List.of(
                new AttributeModifier(Identifier.parse("fixture:add"), 1.5, AttributeModifier.Operation.ADD_VALUE),
                new AttributeModifier(Identifier.parse("fixture:base"), -.25, AttributeModifier.Operation.ADD_MULTIPLIED_BASE),
                new AttributeModifier(Identifier.parse("fixture:total"), .75, AttributeModifier.Operation.ADD_MULTIPLIED_TOTAL));
            var holder = BuiltInRegistries.ATTRIBUTE.wrapAsHolder(attribute);
            attributes.add(new ClientboundUpdateAttributesPacket.AttributeSnapshot(holder, ranged.getDefaultValue(), modifiers));
        }

        var attributeWires = new JsonArray();
        var packet = make(GAME + "ClientboundUpdateAttributesPacket", new String[]{"int", "java.util.List"}, 301, attributes);
        capture(attributeWires, packet);
        output.add("update_attributes", attributeWires);
        output.add("attribute_ranges", ranges);
    }

    static void extraPackets(JsonObject output) throws Exception {
        var picks = new JsonArray();
        capture(picks, new ServerboundPickItemFromBlockPacket(new BlockPos(-12, 70, 23), true));
        output.add("pick_item_from_block", picks);
        var bytes = new byte[256];
        for (int i = 0; i < bytes.length; i++) {
            bytes[i] = (byte) i;
        }
        var signature = new net.minecraft.network.chat.MessageSignature(bytes);
        var packed = new net.minecraft.network.chat.MessageSignature.Packed(signature);
        var deletes = new JsonArray();
        capture(deletes, new ClientboundDeleteChatPacket(packed));
        capture(deletes, new ClientboundDeleteChatPacket(new net.minecraft.network.chat.MessageSignature.Packed(301)));
        output.add("delete_chat", deletes);
        var bits = new BitSet();
        bits.set(0);
        bits.set(3);
        bits.set(19);
        var seen = new net.minecraft.network.chat.LastSeenMessages.Update(301, bits, (byte)77);
        var instant = java.time.Instant.ofEpochMilli(1767000123456L);
        var chats = new JsonArray();
        capture(chats, new ServerboundChatPacket("Hello", instant, -918273645L, Optional.of(signature), seen));
        capture(chats, new ServerboundChatPacket("Hello", instant, -918273645L, Optional.empty(), seen));
        output.add("chat", chats);
        var commands = new JsonArray();
        var arguments = new net.minecraft.commands.arguments.ArgumentSignatures(List.of(
            new net.minecraft.commands.arguments.ArgumentSignatures.Entry("message", signature)));
        capture(commands, new ServerboundChatCommandSignedPacket("say Hello", instant, -918273645L, arguments, seen));
        output.add("chat_command_signed", commands);

        var registry = new net.minecraft.core.MappedRegistry<net.minecraft.network.chat.ChatType>(Registries.CHAT_TYPE, com.mojang.serialization.Lifecycle.stable());
        var decoration = net.minecraft.network.chat.ChatTypeDecoration.withSender("chat.type.text");
        var chatType = registry.register(net.minecraft.network.chat.ChatType.CHAT,
            new net.minecraft.network.chat.ChatType(decoration, decoration), net.minecraft.core.RegistrationInfo.BUILT_IN);
        registry.freeze();
        var registries = new ArrayList<net.minecraft.core.Registry<?>>();
        ((RegistryAccess)access).registries().forEach(entry -> registries.add(entry.value()));
        registries.add(registry);
        var damageRegistry = new net.minecraft.core.MappedRegistry<net.minecraft.world.damagesource.DamageType>(Registries.DAMAGE_TYPE, com.mojang.serialization.Lifecycle.stable());
        var damageType = damageRegistry.register(net.minecraft.world.damagesource.DamageTypes.ARROW,
            new net.minecraft.world.damagesource.DamageType("arrow", .1f), net.minecraft.core.RegistrationInfo.BUILT_IN);
        damageRegistry.freeze();
        registries.add(damageRegistry);
        access = new RegistryAccess.ImmutableRegistryAccess(registries);
        var damage = new JsonArray();
        capture(damage, new ClientboundDamageEventPacket(301, damageType, 2, 3, Optional.of(new Vec3(-12.25, 64.5, 31.875))));
        capture(damage, new ClientboundDamageEventPacket(301, damageType, -1, -1, Optional.empty()));
        output.add("damage_event", damage);
        var playerChats = new JsonArray();
        var disguised = new JsonArray();
        for (int i = 0; i < 3; i++) {
            var body = new net.minecraft.network.chat.SignedMessageBody.Packed("Hello", instant, -918273645L,
                new net.minecraft.network.chat.LastSeenMessages.Packed(List.of(packed, new net.minecraft.network.chat.MessageSignature.Packed(301))));
            var mask = filterMask(i);
            var bound = new net.minecraft.network.chat.ChatType.Bound(chatType, Component.literal("Alex"), i == 0 ? Optional.empty() : Optional.of(Component.literal("Steve")));
            if (i < 2) capture(disguised, new ClientboundDisguisedChatPacket(Component.literal("Hello"), bound));
            capture(playerChats, new ClientboundPlayerChatPacket(7, UUID.fromString("01234567-89ab-cdef-0123-456789abcdef"), 301,
                i == 0 ? Optional.empty() : Optional.of(signature), body,
                i == 0 ? Optional.empty() : Optional.of(Component.literal("Unsigned")), mask, bound));
        }
        output.add("player_chat", playerChats);
        output.add("disguised_chat", disguised);

        var ops = RegistryOps.create(JsonOps.INSTANCE, (RegistryAccess)access);
        var advancement = Advancement.CODEC.parse(ops, JsonParser.parseString("{\"criteria\":{\"alternate\":{\"trigger\":\"minecraft:impossible\"},\"first\":{\"trigger\":\"minecraft:impossible\"},\"second\":{\"trigger\":\"minecraft:impossible\"}},\"requirements\":[[\"first\",\"alternate\"],[\"second\"]],\"sends_telemetry_event\":true}")).getOrThrow();
        var progress = new AdvancementProgress();
        progress.update(advancement.requirements());
        progress.grantProgress("first");
        Vanilla.field("net.minecraft.advancements.CriterionProgress", "obtained").set(progress.getCriterion("first"), java.time.Instant.parse("2026-01-02T03:04:05Z"));
        var id = Identifier.parse("probe:progress");
        output.addProperty("progress_advancements", encode(ClientboundUpdateAdvancementsPacket.class.getName(), "STREAM_CODEC",
            new ClientboundUpdateAdvancementsPacket(true, List.of(new ClientboundUpdateAdvancementsPacket.PositionedAdvancement(new AdvancementHolder(id, advancement), 0, 0)), Set.of(), Map.of(id, progress), false)));
    }

    // Recipe slot displays: furnace inputs and a stonecutter choice, then a shaped and a shapeless recipe book entry.
    static void recipePackets(JsonObject output) {
        var stone = net.minecraft.world.item.Items.STONE;
        var rawIron = net.minecraft.world.item.crafting.Ingredient.of(net.minecraft.world.item.Items.RAW_IRON);
        var sets = new LinkedHashMap<ResourceKey<net.minecraft.world.item.crafting.RecipePropertySet>, net.minecraft.world.item.crafting.RecipePropertySet>();
        for (var name : List.of("smithing_base", "smithing_template", "smithing_addition", "furnace_input",
                "blast_furnace_input", "smoker_input", "campfire_input", "brewing_input", "brewing_reagent")) {
            var key = ResourceKey.create(net.minecraft.world.item.crafting.RecipePropertySet.TYPE_KEY, Identifier.withDefaultNamespace(name));
            sets.put(key, net.minecraft.world.item.crafting.RecipePropertySet.create(
                name.equals("furnace_input") || name.equals("blast_furnace_input") ? List.of(rawIron) : List.of()));
        }
        var cut = new net.minecraft.world.item.crafting.SelectableRecipe.SingleInputEntry<net.minecraft.world.item.crafting.StonecutterRecipe>(
            net.minecraft.world.item.crafting.Ingredient.of(stone),
            new net.minecraft.world.item.crafting.SelectableRecipe<>(new net.minecraft.world.item.crafting.display.SlotDisplay.ItemStackSlotDisplay(
                new net.minecraft.world.item.ItemStackTemplate(net.minecraft.world.item.Items.STONE_SLAB, 2)), Optional.empty()));
        var recipes = new JsonArray();
        capture(recipes, new ClientboundUpdateRecipesPacket(sets, new net.minecraft.world.item.crafting.SelectableRecipe.SingleInputSet<>(List.of(cut))));
        output.add("update_recipes", recipes);

        var table = new net.minecraft.world.item.crafting.display.SlotDisplay.ItemSlotDisplay(net.minecraft.world.item.Items.CRAFTING_TABLE);
        // Recipes display their ingredients as Ingredient.display does.
        var eitherIngredient = net.minecraft.world.item.crafting.Ingredient.of(net.minecraft.world.item.Items.COBBLESTONE, net.minecraft.world.item.Items.ANDESITE);
        var stoneSlot = net.minecraft.world.item.crafting.Ingredient.of(stone).display();
        var either = eitherIngredient.display();
        var shaped = new net.minecraft.world.item.crafting.display.ShapedCraftingRecipeDisplay(2, 2,
            List.of(stoneSlot, net.minecraft.world.item.crafting.display.SlotDisplay.Empty.INSTANCE, either, stoneSlot),
            new net.minecraft.world.item.crafting.display.SlotDisplay.ItemStackSlotDisplay(new net.minecraft.world.item.ItemStackTemplate(net.minecraft.world.item.Items.STONE_BRICKS, 4)),
            table);
        var shapeless = new net.minecraft.world.item.crafting.display.ShapelessCraftingRecipeDisplay(List.of(either),
            new net.minecraft.world.item.crafting.display.SlotDisplay.ItemStackSlotDisplay(new net.minecraft.world.item.ItemStackTemplate(net.minecraft.world.item.Items.GRAVEL)),
            table);
        var book = new JsonArray();
        capture(book, new ClientboundRecipeBookAddPacket(List.of(
            new ClientboundRecipeBookAddPacket.Entry(new net.minecraft.world.item.crafting.display.RecipeDisplayEntry(
                new net.minecraft.world.item.crafting.display.RecipeDisplayId(5), shaped, OptionalInt.of(3),
                net.minecraft.world.item.crafting.RecipeBookCategories.CRAFTING_BUILDING_BLOCKS,
                Optional.of(List.of(net.minecraft.world.item.crafting.Ingredient.of(stone), eitherIngredient, net.minecraft.world.item.crafting.Ingredient.of(stone)))), (byte)0),
            new ClientboundRecipeBookAddPacket.Entry(new net.minecraft.world.item.crafting.display.RecipeDisplayEntry(
                new net.minecraft.world.item.crafting.display.RecipeDisplayId(6), shapeless, OptionalInt.empty(),
                net.minecraft.world.item.crafting.RecipeBookCategories.CRAFTING_MISC, Optional.of(List.of(eitherIngredient))), (byte)0)), true));
        output.add("recipe_book_add", book);
    }

    /** The player chat fixture's filter mask: nothing filtered, everything, then 3 of 80 characters. */
    static net.minecraft.network.chat.FilterMask filterMask(int fixture) {
        if (fixture == 0) {
            return net.minecraft.network.chat.FilterMask.PASS_THROUGH;
        }
        if (fixture == 1) {
            return net.minecraft.network.chat.FilterMask.FULLY_FILTERED;
        }

        var mask = new net.minecraft.network.chat.FilterMask(80);
        if (fixture == 2) {
            mask.setFiltered(1);
            mask.setFiltered(63);
            mask.setFiltered(70);
        }
        return mask;
    }

    static JsonArray advancements() {
        var captures = new JsonArray();
        var random = new Random(777);
        var ops = RegistryOps.create(JsonOps.INSTANCE, (RegistryAccess)access);
        for (int sample = 0; sample < 40; sample++) {
            int count = treeSize(sample);
            var holders = new ArrayList<AdvancementHolder>();
            var parents = new int[count];
            for (int i = 0; i < count; i++) {
                if (i == 0) {
                    parents[i] = -1;
                } else if (sample == 1) {
                    parents[i] = (i - 1) / 2;
                } else {
                    parents[i] = random.nextInt(i);
                }
                boolean display = i == 0 || sample == 1 || random.nextInt(5) != 0;
                holders.add(holder(ops, i, parents[i], display));
            }

            // Vanilla keeps children in an identity hash set, so the order it lays this tree out in changes between
            // JVM runs.
            captures.add(layout(holders, parents, false));
        }

        // Summing a tree's offsets in another order than vanilla rounds a position differently in only a few child
        // orders, which vanilla's own orders above rarely reach. Each of these trees, with its children in index
        // order, is one.
        int[][] roundingTrees = {
            {-1, 0, 1, 2, 0, 4, 4, 6, 6, 8, 6, 4, 4, 4, 13, 13, 13, 16, 13, 4, 19},
            {-1, 0, 1, 1, 3, 0, 5, 5, 7, 7, 5, 5, 5, 12, 12, 12, 12, 16, 12, 18, 12, 5, 21}
        };
        for (var parents : roundingTrees) {
            var holders = new ArrayList<AdvancementHolder>();
            for (int i = 0; i < parents.length; i++) {
                holders.add(holder(ops, i, parents[i], true));
            }
            captures.add(layout(holders, parents, true));
        }
        return captures;
    }

    // A single node, a balanced tree of nine, then trees growing by one node per sample.
    static int treeSize(int sample) {
        if (sample == 0) {
            return 1;
        }

        if (sample == 1) {
            return 9;
        }

        return 15 + sample;
    }

    static AdvancementHolder holder(RegistryOps<JsonElement> ops, int index, int parent, boolean display) {
        var json = new JsonObject();
        if (parent >= 0) {
            json.addProperty("parent", "fixture:n" + parent);
        }

        if (display) {
            var info = new JsonObject();
            info.addProperty("title", "Title");
            info.addProperty("description", "Description");
            var icon = new JsonObject();
            icon.addProperty("id", "minecraft:diamond");
            icon.addProperty("count", 3);
            info.add("icon", icon);
            info.addProperty("frame", "goal");
            if (index == 0) {
                info.addProperty("background", "minecraft:gui/advancements/backgrounds/stone");
            }
            json.add("display", info);
        }

        json.add("criteria", JsonParser.parseString("{\"done\":{\"trigger\":\"minecraft:impossible\"}}"));
        var advancement = Advancement.CODEC.parse(ops, json).getOrThrow();
        return new AdvancementHolder(Identifier.parse("fixture:n" + index), advancement);
    }

    /**
     * Lays out one tree with vanilla's server layout and records its definitions in the order the layout visited
     * them. Unless {@code ordered}, the nodes are vanilla's own; otherwise each node's children are iterated in index
     * order.
     */
    static JsonObject layout(List<AdvancementHolder> holders, int[] parents, boolean ordered) {
        var nodes = new ArrayList<AdvancementNode>();
        for (int i = 0; i < holders.size(); i++) {
            var parent = parents[i] < 0 ? null : nodes.get(parents[i]);
            if (ordered) {
                nodes.add(new OrderedNode(holders.get(i), parent));
            } else {
                nodes.add(new AdvancementNode(holders.get(i), parent));
            }
            if (parent != null) {
                parent.addChild(nodes.get(i));
            }
        }

        TreeNodePosition.run(nodes.getFirst());
        var visited = new ArrayList<AdvancementNode>();
        visit(nodes.getFirst(), visited);
        var definitions = new JsonArray();
        for (var node : visited) {
            var definition = new JsonObject();
            definition.addProperty("id", node.holder().id().toString());
            var parent = node.parent() == null ? "" : node.parent().holder().id().toString();
            definition.addProperty("parent", parent);
            definition.addProperty("display", node.advancement().display().isPresent());
            definition.addProperty("x", node.x());
            definition.addProperty("y", node.y());
            definitions.add(definition);
        }

        var row = new JsonObject();
        row.add("definitions", definitions);
        var positioned = visited.stream().map(ClientboundUpdateAdvancementsPacket.PositionedAdvancement::fromNode);
        var packet = new ClientboundUpdateAdvancementsPacket(true, positioned.toList(),
            Set.of(Identifier.parse("fixture:removed")), Map.of(), true);
        var codec = ClientboundUpdateAdvancementsPacket.class.getName();
        row.addProperty("wire", encode(codec, "STREAM_CODEC", packet));
        return row;
    }

    /** Iterates children in the order they were added, unlike vanilla's identity hash set. */
    static final class OrderedNode extends AdvancementNode {
        private final List<AdvancementNode> ordered = new ArrayList<>();

        OrderedNode(AdvancementHolder holder, AdvancementNode parent) {
            super(holder, parent);
        }

        @Override
        public Iterable<AdvancementNode> children() {
            return ordered;
        }

        @Override
        public void addChild(AdvancementNode child) {
            ordered.add(child);
        }
    }

    static void visit(AdvancementNode node, List<AdvancementNode> ordered) {
        ordered.add(node);
        for (var child : node.children()) visit(child, ordered);
    }
}
