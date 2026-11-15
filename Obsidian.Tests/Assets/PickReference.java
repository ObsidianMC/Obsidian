import net.minecraft.SharedConstants;
import net.minecraft.server.Bootstrap;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.core.BlockPos;
import com.google.gson.*;
import java.nio.file.*;

// Records default-state clone results from the pinned vanilla server jar and its existing libraries.
// Run with Java 25: java -cp <server-26.3.jar;cached library jars> PickReference.java <output.json>
// The LevelReader supplies registries only; these cases do not depend on block-entity contents.
class PickReference {
    private static final String[] BLOCKS = {
        "stone",
        "wheat",
        "carrots",
        "potatoes",
        "beetroots",
        "cocoa",
        "torchflower_crop",
        "pitcher_crop",
        "melon_stem",
        "attached_melon_stem",
        "pumpkin_stem",
        "attached_pumpkin_stem",
        "bamboo_sapling",
        "big_dripleaf_stem",
        "cave_vines",
        "cave_vines_plant",
        "sweet_berry_bush",
        "kelp_plant",
        "twisting_vines_plant",
        "weeping_vines_plant",
        "tall_seagrass",
        "redstone_wire",
        "tripwire",
        "piston_head",
        "candle_cake",
        "blue_candle_cake",
        "potted_dandelion",
        "potted_oak_sapling",
        "potted_azalea_bush",
        "oak_wall_sign",
        "oak_wall_hanging_sign",
        "skeleton_wall_skull",
        "dragon_wall_head",
        "wall_torch",
        "soul_wall_torch",
        "redstone_wall_torch",
        "nether_portal",
        "end_portal",
        "end_gateway",
        "frosted_ice",
        "moving_piston",
        "water",
        "lava",
        "air",
        "light",
        "test_block"
    };

    public static void main(String[] args) throws Exception {
        SharedConstants.tryDetectVersion();
        Bootstrap.bootStrap();
        var world = net.minecraft.data.registries.VanillaRegistries.createWorldLookup();
        var access = net.minecraft.data.registries.VanillaRegistries.createReloadableLookup(world);
        for (var pending : BuiltInRegistries.DATA_COMPONENT_INITIALIZERS.build(access)) {
            pending.apply();
        }

        var registries = net.minecraft.core.RegistryAccess.fromRegistryOfRegistries(BuiltInRegistries.REGISTRY);
        var level = (net.minecraft.world.level.LevelReader) java.lang.reflect.Proxy.newProxyInstance(
            PickReference.class.getClassLoader(), new Class<?>[]{net.minecraft.world.level.LevelReader.class},
            (proxy, method, values) -> method.getName().equals("registryAccess") ? registries : null);
        var output = new JsonObject();
        output.addProperty("version", "26.3");
        var cases = new JsonArray();

        for (var name : BLOCKS) {
            var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse("minecraft:" + name));
            var state = block.defaultBlockState();
            var stack = state.getCloneItemStack(level, BlockPos.ZERO, false);
            var row = new JsonObject();
            row.addProperty("block", "minecraft:" + name);
            var item = stack.isEmpty() ? "minecraft:air" : BuiltInRegistries.ITEM.getKey(stack.getItem()).toString();
            row.addProperty("item", item);
            cases.add(row);
        }

        output.add("cases", cases);
        Files.writeString(Path.of(args[0]), new GsonBuilder().setPrettyPrinting().create().toJson(output) + "\n");
        System.out.println("Captured " + cases.size() + " block pick results from vanilla 26.3");
    }
}
