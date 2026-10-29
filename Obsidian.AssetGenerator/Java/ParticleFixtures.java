import com.google.gson.*;
import com.mojang.serialization.JsonOps;
import io.netty.buffer.*;
import java.nio.file.*;
import net.minecraft.SharedConstants;
import net.minecraft.client.renderer.WeatherEffectRenderer;
import net.minecraft.core.RegistryAccess;
import net.minecraft.core.particles.ParticleTypes;
import net.minecraft.core.particles.VibrationParticleOption;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.resources.RegistryOps;
import net.minecraft.server.Bootstrap;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.gameevent.EntityPositionSource;

/** Build-time particle fixture capture. Runs against the installed client and its matching libraries. */
class ParticleFixtures {
    public static void main(String[] args) throws Exception {
        SharedConstants.tryDetectVersion();
        Bootstrap.bootStrap();
        var access = RegistryAccess.fromRegistryOfRegistries(BuiltInRegistries.REGISTRY);
        var ops = RegistryOps.create(JsonOps.INSTANCE, access);
        var rows = new JsonArray();
        for (var type : BuiltInRegistries.PARTICLE_TYPE) {
            String name = BuiltInRegistries.PARTICLE_TYPE.getKey(type).getPath();
            var input = new JsonObject(); input.addProperty("type", "minecraft:" + name);
            switch (name) {
                case "block", "block_marker", "falling_dust", "dust_pillar", "block_crumble" -> input.addProperty("block_state", "minecraft:stone");
                case "dust" -> { input.addProperty("color", 0x123456); input.addProperty("scale", 1.5); }
                case "dust_color_transition" -> { input.addProperty("from_color", 0x123456); input.addProperty("to_color", 0xabcdef); input.addProperty("scale", .5); }
                case "effect", "instant_effect" -> { input.addProperty("color", 0xabcdef); input.addProperty("power", .75); }
                case "entity_effect", "tinted_leaves", "flash" -> input.addProperty("color", 0x80123456);
                case "dragon_breath" -> input.addProperty("power", .5);
                case "sculk_charge" -> input.addProperty("roll", .25);
                case "shriek" -> input.addProperty("delay", 17);
                case "geyser", "geyser_plume" -> input.addProperty("water_blocks", 3);
                case "geyser_base", "geyser_poof" -> { input.addProperty("water_blocks", 4); input.addProperty("burst_impulse_base", 1.5); }
                case "item" -> input.add("item", JsonParser.parseString("{\"id\":\"minecraft:diamond\",\"count\":3}"));
                case "trail" -> { input.add("target", JsonParser.parseString("[1.25,72,-3.5]")); input.addProperty("color", 0xabcdef); input.addProperty("duration", 20); }
                case "vibration" -> { input.add("destination", JsonParser.parseString("{\"type\":\"minecraft:block\",\"pos\":[-4,72,9]}")); input.addProperty("arrival_in_ticks", 25); }
            }
            var option = ParticleTypes.CODEC.parse(ops, input).getOrThrow();
            var buf = new RegistryFriendlyByteBuf(Unpooled.buffer(), access);
            ParticleTypes.STREAM_CODEC.encode(buf, option);
            var row = new JsonObject(); row.addProperty("id", BuiltInRegistries.PARTICLE_TYPE.getId(type));
            row.addProperty("name", name); row.add("input", input); row.addProperty("wire", ByteBufUtil.hexDump(buf));
            row.addProperty("overrideLimiter", type.getOverrideLimiter()); rows.add(row); buf.release();
        }
        // Entity position sources are network-only; construct through the actual position stream codec.
        var source = Unpooled.buffer(); source.writeByte(42); source.writeFloat(1.25f);
        var entity = EntityPositionSource.STREAM_CODEC.decode(source); source.release();
        var buf = new RegistryFriendlyByteBuf(Unpooled.buffer(), access);
        ParticleTypes.STREAM_CODEC.encode(buf, new VibrationParticleOption(entity, 30));
        var result = new JsonObject(); result.add("particles", rows); result.addProperty("entityVibration", ByteBufUtil.hexDump(buf)); buf.release();
        var columns = new JsonArray();
        // The column methods don't use the renderer's state, and its constructor needs a running client.
        var unsafeField = sun.misc.Unsafe.class.getDeclaredField("theUnsafe"); unsafeField.setAccessible(true);
        var weather = ((sun.misc.Unsafe) unsafeField.get(null)).allocateInstance(WeatherEffectRenderer.class);
        for (var name : new String[]{"createRainColumnInstance", "createSnowColumnInstance"}) {
            var method = WeatherEffectRenderer.class.getDeclaredMethod(name, RandomSource.class, long.class, int.class, int.class, int.class, int.class, int.class, float.class);
            method.setAccessible(true);
            int x=-12,z=7;
            var random = RandomSource.create((long)(x*x*3121+x*45238971 ^ z*z*418711+z*13761));
            var column = (WeatherEffectRenderer.ColumnInstance)method.invoke(weather,random,100L,x,64,82,z,0,.5f);
            var row=new JsonObject();row.addProperty("kind",name.equals("createRainColumnInstance")?"Rain":"Snow");row.addProperty("u",column.uOffset());row.addProperty("v",column.vOffset());columns.add(row);
        }
        result.add("weather",columns);
        Files.writeString(Path.of(args[0]), new GsonBuilder().setPrettyPrinting().create().toJson(result));
        System.out.println("Captured " + rows.size() + " particle types");
    }
}
