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
        capture(teams, ClientboundSetPlayerTeamPacket.createPlayerPacket(team, "Steve", ClientboundSetPlayerTeamPacket.Action.ADD));
        capture(teams, ClientboundSetPlayerTeamPacket.createPlayerPacket(team, "Alex", ClientboundSetPlayerTeamPacket.Action.REMOVE));
        team.setColor(Optional.empty());
        team.setNameTagVisibility(Team.Visibility.NEVER);
        team.setCollisionRule(Team.CollisionRule.PUSH_OWN_TEAM);
        capture(teams, ClientboundSetPlayerTeamPacket.createAddOrModifyPacket(team, false));
        output.add("set_player_team", teams);

        var particles = new JsonArray();
        // Every options-free type is exercised, including all types emitted by gameplay and /particle.
        for (var type : BuiltInRegistries.PARTICLE_TYPE) {
            if (!(type instanceof SimpleParticleType simple)) continue;
            var row = new JsonObject();
            row.addProperty("id", BuiltInRegistries.PARTICLE_TYPE.getId(type));
            row.addProperty("name", BuiltInRegistries.PARTICLE_TYPE.getKey(type).toString());
            var wires = new JsonArray();
            for (var randomization : ClientboundLevelParticlesPacket.RandomizationType.values())
                capture(wires, new ClientboundLevelParticlesPacket(simple, true, false, -12.25, 64.5, 31.875,
                    .25f, .5f, .75f, 1f, 2f, 3f, 301, randomization));
            row.add("wire", wires);
            particles.add(row);
        }
        output.add("level_particles", particles);
        var explosions = new JsonArray();
        var sound = net.minecraft.core.Holder.direct(SoundEvent.createFixedRangeEvent(Identifier.parse("fixture:boom"), 24f));
        var debris = WeightedList.<ExplosionParticleInfo>builder().add(new ExplosionParticleInfo(ParticleTypes.SMOKE, .5f, .75f), 3).build();
        capture(explosions, new ClientboundExplodePacket(new Vec3(-12.25, 64.5, 31.875), 4f, 301,
            Optional.of(new Vec3(.25, -.5, 1.5)), ParticleTypes.EXPLOSION_EMITTER, sound, debris, true));
        capture(explosions, new ClientboundExplodePacket(new Vec3(-12.25, 64.5, 31.875), 4f, 0,
            Optional.empty(), ParticleTypes.EXPLOSION_EMITTER, sound, WeightedList.of(), false));
        output.add("explode", explosions);
        var transientBlocks = new JsonArray();
        capture(transientBlocks, new ClientboundAddTransientBlockPacket(new BlockPos(-12, 70, 23), net.minecraft.world.level.block.Blocks.STONE.defaultBlockState()));
        output.add("add_transient_block", transientBlocks);
        var disk = new JsonArray();
        capture(disk, ClientboundLowDiskSpaceWarningPacket.INSTANCE);
        output.add("low_disk_space_warning", disk);

        var rules = new GameRules(BuiltInRegistries.GAME_RULE.stream().toList());
        var values = new LinkedHashMap<ResourceKey<GameRule<?>>, String>();
        var bounds = new JsonObject();
        var parsing = new JsonArray();
        for (var rule : BuiltInRegistries.GAME_RULE.stream().sorted(Comparator.comparing(r -> r.getIdentifier().toString())).toList()) {
            var key = ResourceKey.create(Registries.GAME_RULE, rule.getIdentifier());
            values.put(key, rules.getAsString(rule));
            if (rule.argument() instanceof com.mojang.brigadier.arguments.IntegerArgumentType integer) {
                var limits = new JsonArray(); limits.add(integer.getMinimum()); limits.add(integer.getMaximum());
                bounds.add(rule.id(), limits);
            }
            for (String value : List.of("true", "false", "TRUE", "1", "-1", "0", "+1", "2147483647", "2147483648", "1 ", " 1")) {
                var row = new JsonArray(); row.add(rule.getIdentifier().toString()); row.add(value);
                row.add(rule.deserialize(value).result().isPresent()); parsing.add(row);
            }
        }
        var ruleWires = new JsonArray();
        capture(ruleWires, new ClientboundGameRuleValuesPacket(values));
        output.add("game_rule_values", ruleWires);
        output.add("game_rule_parsing", parsing);
        output.add("game_rule_bounds", bounds);
        var edits = new JsonArray();
        capture(edits, new ServerboundSetGameRulePacket(List.of(
            new ServerboundSetGameRulePacket.Entry(ResourceKey.create(Registries.GAME_RULE, Identifier.parse("minecraft:keep_inventory")), "true"),
            new ServerboundSetGameRulePacket.Entry(ResourceKey.create(Registries.GAME_RULE, Identifier.parse("minecraft:random_tick_speed")), "17"))));
        output.add("set_game_rule", edits);

        var attributes = new ArrayList<ClientboundUpdateAttributesPacket.AttributeSnapshot>();
        var ranges = new JsonArray();
        for (var attribute : BuiltInRegistries.ATTRIBUTE) {
            var ranged = (RangedAttribute)attribute;
            var row = new JsonArray(); row.add(BuiltInRegistries.ATTRIBUTE.getKey(attribute).toString());
            row.add(ranged.getMinValue()); row.add(ranged.getMaxValue()); row.add(ranged.isClientSyncable()); row.add(ranged.getDefaultValue()); ranges.add(row);
            attributes.add(new ClientboundUpdateAttributesPacket.AttributeSnapshot(BuiltInRegistries.ATTRIBUTE.wrapAsHolder(attribute), ranged.getDefaultValue(), List.of(
                new AttributeModifier(Identifier.parse("fixture:add"), 1.5, AttributeModifier.Operation.ADD_VALUE),
                new AttributeModifier(Identifier.parse("fixture:base"), -.25, AttributeModifier.Operation.ADD_MULTIPLIED_BASE),
                new AttributeModifier(Identifier.parse("fixture:total"), .75, AttributeModifier.Operation.ADD_MULTIPLIED_TOTAL))));
        }
        var attributeWires = new JsonArray();
        capture(attributeWires, make(GAME + "ClientboundUpdateAttributesPacket", new String[]{"int", "java.util.List"}, 301, attributes));
        output.add("update_attributes", attributeWires);
        output.add("attribute_ranges", ranges);
        output.add("update_advancements", advancements());
        extraPackets(output);
        recipePackets(output);
        Files.writeString(Path.of(args[0]), new GsonBuilder().setPrettyPrinting().create().toJson(output));
    }

    static void extraPackets(JsonObject output) throws Exception {
        var picks = new JsonArray();
        capture(picks, new ServerboundPickItemFromBlockPacket(new BlockPos(-12, 70, 23), true));
        output.add("pick_item_from_block", picks);
        var bytes = new byte[256];
        for (int i = 0; i < bytes.length; i++) bytes[i] = (byte)i;
        var signature = new net.minecraft.network.chat.MessageSignature(bytes);
        var packed = new net.minecraft.network.chat.MessageSignature.Packed(signature);
        var deletes = new JsonArray();
        capture(deletes, new ClientboundDeleteChatPacket(packed));
        capture(deletes, new ClientboundDeleteChatPacket(new net.minecraft.network.chat.MessageSignature.Packed(301)));
        output.add("delete_chat", deletes);
        var bits = new BitSet(); bits.set(0); bits.set(3); bits.set(19);
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
        damageRegistry.freeze(); registries.add(damageRegistry);
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
            var mask = i == 0 ? net.minecraft.network.chat.FilterMask.PASS_THROUGH : i == 1 ? net.minecraft.network.chat.FilterMask.FULLY_FILTERED : new net.minecraft.network.chat.FilterMask(80);
            if (i == 2) { mask.setFiltered(1); mask.setFiltered(63); mask.setFiltered(70); }
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
        var progress = new AdvancementProgress(); progress.update(advancement.requirements()); progress.grantProgress("first");
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
        net.minecraft.world.item.crafting.display.SlotDisplay stoneSlot = new net.minecraft.world.item.crafting.display.SlotDisplay.ItemSlotDisplay(stone);
        net.minecraft.world.item.crafting.display.SlotDisplay either = new net.minecraft.world.item.crafting.display.SlotDisplay.Composite(List.of(
            new net.minecraft.world.item.crafting.display.SlotDisplay.ItemSlotDisplay(net.minecraft.world.item.Items.COBBLESTONE),
            new net.minecraft.world.item.crafting.display.SlotDisplay.ItemSlotDisplay(net.minecraft.world.item.Items.ANDESITE)));
        var eitherIngredient = net.minecraft.world.item.crafting.Ingredient.of(net.minecraft.world.item.Items.COBBLESTONE, net.minecraft.world.item.Items.ANDESITE);
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

    static JsonArray advancements() {
        var captures = new JsonArray();
        var random = new Random(777);
        // Ordered child lists are captured explicitly: vanilla's identity hash set iteration isn't stable between JVMs.
        for (int sample = 0; sample < 40; sample++) {
            int count = sample == 0 ? 1 : sample == 1 ? 9 : 15 + sample;
            var nodes = new ArrayList<AdvancementNode>();
            var ops = RegistryOps.create(JsonOps.INSTANCE, (RegistryAccess)access);
            for (int i = 0; i < count; i++) {
                int parent = i == 0 ? -1 : sample == 1 ? (i - 1) / 2 : random.nextInt(i);
                var json = new JsonObject();
                if (parent >= 0) json.addProperty("parent", "fixture:n" + parent);
                boolean display = i == 0 || sample == 1 || random.nextInt(5) != 0;
                if (display) {
                    var info = JsonParser.parseString("{\"title\":\"Title\",\"description\":\"Description\",\"icon\":{\"id\":\"minecraft:diamond\",\"count\":3},\"frame\":\"goal\"}").getAsJsonObject();
                    if (i == 0) info.addProperty("background", "minecraft:gui/advancements/backgrounds/stone");
                    json.add("display", info);
                }
                json.add("criteria", JsonParser.parseString("{\"done\":{\"trigger\":\"minecraft:impossible\"}}"));
                var advancement = Advancement.CODEC.parse(ops, json).getOrThrow();
                var node = new AdvancementNode(new AdvancementHolder(Identifier.parse("fixture:n" + i), advancement), parent < 0 ? null : nodes.get(parent));
                nodes.add(node);
                if (parent >= 0) nodes.get(parent).addChild(node);
            }
            TreeNodePosition.run(nodes.getFirst());
            var ordered = new ArrayList<AdvancementNode>();
            visit(nodes.getFirst(), ordered);
            var row = new JsonObject();
            var definitions = new JsonArray();
            for (var node : ordered) {
                var definition = new JsonObject();
                definition.addProperty("id", node.holder().id().toString());
                definition.addProperty("parent", node.parent() == null ? "" : node.parent().holder().id().toString());
                definition.addProperty("display", node.advancement().display().isPresent());
                definition.addProperty("x", node.x()); definition.addProperty("y", node.y()); definitions.add(definition);
            }
            row.add("definitions", definitions);
            var positioned = ordered.stream().map(ClientboundUpdateAdvancementsPacket.PositionedAdvancement::fromNode).toList();
            row.addProperty("wire", encode(ClientboundUpdateAdvancementsPacket.class.getName(), "STREAM_CODEC",
                new ClientboundUpdateAdvancementsPacket(true, positioned, Set.of(Identifier.parse("fixture:removed")), Map.of(), true)));
            captures.add(row);
        }
        return captures;
    }
    static void visit(AdvancementNode node, List<AdvancementNode> ordered) {
        ordered.add(node);
        for (var child : node.children()) visit(child, ordered);
    }
}
