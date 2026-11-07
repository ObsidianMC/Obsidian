import com.google.gson.GsonBuilder;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;
import net.minecraft.core.Holder;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.tags.FluidTags;
import net.minecraft.tags.TagKey;
import net.minecraft.world.effect.MobEffect;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.ai.attributes.Attribute;
import net.minecraft.world.entity.ai.attributes.AttributeInstance;
import net.minecraft.world.entity.ai.attributes.AttributeMap;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.enchantment.EnchantmentHelper;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.material.Fluid;

/**
 * Records vanilla's {@code BlockState.getDestroyProgress} for a player: how much of a block one tick of mining breaks,
 * from the held tool, its enchantments, the helmet's, the player's effects, water at their eyes and whether they
 * stand on the ground. A progress of 1 or more breaks the block on the first tick, which the client then predicts
 * without ever telling the server it finished.
 */
public final class MiningFixtures {
    public static void main(String[] args) throws Exception {
        var version = VanillaDumper.bootstrap();
        var registries = (HolderLookup.Provider)VanillaDumper.vanillaRegistries();
        var root = JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonObject();
        root.addProperty("version", version);

        for (var entry : root.getAsJsonArray("cases")) {
            var input = entry.getAsJsonObject();
            var player = player(input, registries);
            var state = BuiltInRegistries.BLOCK.getValue(Identifier.parse(input.get("block").getAsString()))
                .defaultBlockState();

            // JSON numbers can't hold infinity (a block with no hardness), so non-finite values are strings.
            var progress = state.getDestroyProgress(player, null, null);
            if (Float.isFinite(progress)) {
                input.addProperty("progress", progress);
            } else {
                input.addProperty("progress", Float.toString(progress));
            }
        }

        Files.writeString(Path.of(args[0]), new GsonBuilder().setPrettyPrinting().disableHtmlEscaping()
            .create().toJson(root) + "\n");
    }

    // A player with only what mining reads: the attributes of a new player with the equipment's enchantment
    // modifiers applied as vanilla applies them, its effects, the held item and its ground and water state.
    private static Player player(JsonObject input, HolderLookup.Provider registries) throws Exception {
        var player = (ProbePlayer)allocate(ProbePlayer.class);
        player.attributes = new AttributeMap(Player.createAttributes().build());
        player.effects = new HashMap<>();
        player.grounded = !input.has("onGround") || input.get("onGround").getAsBoolean();
        player.eyeInWater = input.has("eyeInWater") && input.get("eyeInWater").getAsBoolean();

        var held = stack(input, "item", registries);
        var helmet = stack(input, "helmet", registries);
        EnchantmentHelper.forEachModifier(held, EquipmentSlot.MAINHAND, (attribute, modifier) ->
            player.attributes.getInstance(attribute).addTransientModifier(modifier));
        EnchantmentHelper.forEachModifier(helmet, EquipmentSlot.HEAD, (attribute, modifier) ->
            player.attributes.getInstance(attribute).addTransientModifier(modifier));

        var inventory = (ProbeInventory)allocate(ProbeInventory.class);
        inventory.selected = held;
        var field = Player.class.getDeclaredField("inventory");
        field.setAccessible(true);
        field.set(player, inventory);

        if (input.has("effects")) {
            for (var effect : input.getAsJsonObject("effects").entrySet()) {
                var holder = BuiltInRegistries.MOB_EFFECT.get(Identifier.parse(effect.getKey())).orElseThrow();
                player.effects.put(holder, new MobEffectInstance(holder, 200, effect.getValue().getAsInt()));
            }
        }

        return player;
    }

    // An item from the case: {"id": ..., "enchantments": {"minecraft:efficiency": 5}}, or nothing in that slot.
    private static ItemStack stack(JsonObject input, String key, HolderLookup.Provider registries) {
        if (!input.has(key)) {
            return ItemStack.EMPTY;
        }

        var json = input.getAsJsonObject(key);
        var stack = new ItemStack(BuiltInRegistries.ITEM.getValue(Identifier.parse(json.get("id").getAsString())));
        if (json.has("enchantments")) {
            var enchantments = registries.lookupOrThrow(Registries.ENCHANTMENT);
            for (var enchantment : json.getAsJsonObject("enchantments").entrySet()) {
                var id = ResourceKey.create(Registries.ENCHANTMENT, Identifier.parse(enchantment.getKey()));
                stack.enchant(enchantments.getOrThrow(id), enchantment.getValue().getAsInt());
            }
        }

        return stack;
    }

    private static Object allocate(Class<?> type) throws Exception {
        var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        field.setAccessible(true);
        var unsafe = field.get(null);
        return unsafe.getClass().getMethod("allocateInstance", Class.class).invoke(unsafe, type);
    }

    private static final class ProbeInventory extends Inventory {
        ItemStack selected;

        private ProbeInventory() {
            super(null, null);
            throw new UnsupportedOperationException();
        }

        @Override public ItemStack getSelectedItem() {
            return selected;
        }
    }

    private static final class ProbePlayer extends Player {
        AttributeMap attributes;
        Map<Holder<MobEffect>, MobEffectInstance> effects;
        boolean grounded;
        boolean eyeInWater;

        private ProbePlayer() {
            super(null, null);
            throw new UnsupportedOperationException();
        }

        @Override public AttributeMap getAttributes() {
            return attributes;
        }

        @Override public AttributeInstance getAttribute(Holder<Attribute> attribute) {
            return attributes.getInstance(attribute);
        }

        @Override public double getAttributeValue(Holder<Attribute> attribute) {
            return attributes.getValue(attribute);
        }

        @Override public boolean hasEffect(Holder<MobEffect> effect) {
            return effects.containsKey(effect);
        }

        @Override public MobEffectInstance getEffect(Holder<MobEffect> effect) {
            return effects.get(effect);
        }

        @Override public boolean onGround() {
            return grounded;
        }

        @Override public boolean isEyeInFluid(TagKey<Fluid> fluid) {
            return eyeInWater && fluid == FluidTags.WATER;
        }

        @Override public net.minecraft.world.level.GameType gameMode() {
            return net.minecraft.world.level.GameType.SURVIVAL;
        }
    }
}
