import com.google.gson.*;
import io.netty.buffer.*;
import java.nio.file.*;
import com.mojang.serialization.JsonOps;
import net.minecraft.core.MappedRegistry;
import net.minecraft.core.Registry;
import net.minecraft.core.RegistryAccess;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.protocol.game.ClientboundRecipeBookAddPacket;
import net.minecraft.resources.RegistryOps;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.crafting.Ingredient;
import net.minecraft.world.item.crafting.RecipeBookCategories;
import net.minecraft.world.item.crafting.display.RecipeDisplay;
import net.minecraft.world.item.crafting.display.RecipeDisplayEntry;
import net.minecraft.world.item.crafting.display.RecipeDisplayId;
import net.minecraft.world.item.crafting.display.SlotDisplay;
import net.minecraft.world.item.equipment.trim.TrimPattern;

/** Build-time HUD capture using the installed client's public codecs; no game assets are exported. */
class HudFixtures {
    public static void main(String[] args) throws Exception {
        // Binds the item tags too, which tag slot displays refer to.
        VanillaDumper.bootstrap();
        var trimPatterns = new MappedRegistry<TrimPattern>(Registries.TRIM_PATTERN, com.mojang.serialization.Lifecycle.stable());
        trimPatterns.freeze();
        var registries = new java.util.ArrayList<Registry<?>>();
        RegistryAccess.fromRegistryOfRegistries(BuiltInRegistries.REGISTRY).registries().forEach(entry -> registries.add(entry.value()));
        registries.add(trimPatterns);
        var access = new RegistryAccess.ImmutableRegistryAccess(registries);
        var ops = RegistryOps.create(JsonOps.INSTANCE, access);
        var effects = new JsonArray();
        for (var effect : BuiltInRegistries.MOB_EFFECT) {
            var row = new JsonObject();
            row.addProperty("id", BuiltInRegistries.MOB_EFFECT.getId(effect));
            row.addProperty("name", BuiltInRegistries.MOB_EFFECT.getKey(effect).getPath());
            row.addProperty("color", effect.getColor());
            row.addProperty("beneficial", effect.isBeneficial());
            effects.add(row);
        }
        String item = "{\"type\":\"minecraft:item\",\"item\":\"minecraft:oak_planks\"}";
        String result = "{\"type\":\"minecraft:item_stack\",\"item\":{\"id\":\"minecraft:crafting_table\",\"count\":1}}";
        String station = "{\"type\":\"minecraft:item\",\"item\":\"minecraft:crafting_table\"}";
        String tag = "{\"type\":\"minecraft:tag\",\"tag\":\"#minecraft:planks\"}";
        String composite = "{\"type\":\"minecraft:composite\",\"contents\":[" + item + "," + tag + "]}";
        String remainder = "{\"type\":\"minecraft:with_remainder\",\"input\":" + item + ",\"remainder\":{\"type\":\"minecraft:empty\"}}";
        String tail = ",\"result\":" + result + ",\"crafting_station\":" + station + "}";
        String[] inputs = {
            "{\"type\":\"minecraft:crafting_shapeless\",\"ingredients\":[" + composite + "," + remainder + "]" + tail,
            "{\"type\":\"minecraft:crafting_shaped\",\"width\":2,\"height\":2,\"ingredients\":[" + String.join(",", item,item,item,item) + "]" + tail,
            "{\"type\":\"minecraft:furnace\",\"ingredient\":" + tag + ",\"fuel\":{\"type\":\"minecraft:any_fuel\"},\"duration\":200,\"experience\":0.7" + tail,
            "{\"type\":\"minecraft:stonecutter\",\"input\":" + item + tail,
            "{\"type\":\"minecraft:smithing\",\"template\":" + item + ",\"base\":" + item + ",\"addition\":" + tag + tail
        };
        var recipes = new JsonArray();
        for (String input : inputs) {
            var json = JsonParser.parseString(input);
            var recipe = RecipeDisplay.CODEC.parse(ops, json).getOrThrow();
            var buffer = new RegistryFriendlyByteBuf(Unpooled.buffer(), access);
            RecipeDisplay.STREAM_CODEC.encode(buffer, recipe);
            var row = new JsonObject(); row.add("input", json); row.addProperty("wire", ByteBufUtil.hexDump(buffer));
            buffer.clear();
            var requirements = java.util.Optional.of(java.util.List.of(Ingredient.of(Items.OAK_PLANKS, Items.BIRCH_PLANKS), Ingredient.of(Items.OAK_PLANKS)));
            var entry = new RecipeDisplayEntry(new RecipeDisplayId(42 + recipes.size()), recipe, java.util.OptionalInt.of(7), RecipeBookCategories.CRAFTING_MISC, requirements);
            ((ClientboundRecipeBookAddPacket.Entry)null).STREAM_CODEC.encode(buffer, new ClientboundRecipeBookAddPacket.Entry(entry, true, true));
            row.addProperty("entry", ByteBufUtil.hexDump(buffer));
            recipes.add(row); buffer.release();
        }
        var resultJson = new JsonObject(); resultJson.add("effects", effects); resultJson.add("recipes", recipes);
        var trimInput = JsonParser.parseString("{\"type\":\"minecraft:smithing_trim\",\"base\":" + item + ",\"material\":" + item + ",\"pattern\":{\"asset_id\":\"minecraft:sentry\",\"description\":{\"text\":\"Fixture\"},\"decal\":false}}");
        var trim = SlotDisplay.CODEC.parse(ops, trimInput).getOrThrow();
        var trimBuffer = new RegistryFriendlyByteBuf(Unpooled.buffer(), access);
        SlotDisplay.STREAM_CODEC.encode(trimBuffer, trim);
        resultJson.addProperty("trimSlot", ByteBufUtil.hexDump(trimBuffer)); trimBuffer.release();
        Files.writeString(Path.of(args[0]), new GsonBuilder().setPrettyPrinting().create().toJson(resultJson));
    }
}
