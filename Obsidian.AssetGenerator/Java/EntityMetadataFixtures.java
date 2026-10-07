import com.google.gson.*;
import com.mojang.serialization.*;
import io.netty.buffer.*;
import java.nio.file.*;
import java.util.*;
import java.util.stream.*;

/** Captures metadata serializer payloads with vanilla's registry-aware stream codecs. Run manually for parity tests. */
class EntityMetadataFixtures {
    static VanillaDumper.Mojang m;
    static Object access;
    static Object call(String owner, String name, Object target, String[] types, Object... args) {
        return VanillaDumper.Mojang.call(m.method(owner, name, types), target, args);
    }
    static Object holder(String name) {
        var registry = call("net.minecraft.core.RegistryAccess", "lookupOrThrow", access,
            new String[]{"net.minecraft.resources.ResourceKey"}, m.get("net.minecraft.core.registries.Registries", name));
        return ((Optional<?>)call("net.minecraft.core.Registry", "getAny", registry, new String[]{})).orElseThrow();
    }
    public static void main(String[] args) throws Exception {
        m = VanillaDumper.Mojang.read(Path.of(args[0]));
        call("net.minecraft.SharedConstants", "tryDetectVersion", null, new String[]{});
        call("net.minecraft.server.Bootstrap", "bootStrap", null, new String[]{});
        var builtin = m.get("net.minecraft.core.registries.BuiltInRegistries", "REGISTRY");
        var base = new ArrayList<Object>();
        ((Iterable<?>)builtin).forEach(base::add);
        var pack = call("net.minecraft.server.packs.repository.ServerPacksSource", "createVanillaPackSource", null, new String[]{});
        var resources = VanillaDumper.Mojang.create(m.constructor("net.minecraft.server.packs.resources.MultiPackResourceManager", "net.minecraft.server.packs.PackType", "java.util.List"), m.get("net.minecraft.server.packs.PackType", "SERVER_DATA"), List.of(pack));
        for (var reg : base) {
            var pending = (Optional<?>)call("net.minecraft.tags.TagLoader", "loadPendingTags", null,
                new String[]{"net.minecraft.server.packs.resources.ResourceManager", "net.minecraft.core.Registry"}, resources, reg);
            if (pending.isPresent()) call("net.minecraft.core.Registry$PendingTags", "apply", pending.get(), new String[]{});
        }
        var dynamic = call("net.minecraft.resources.RegistryDataLoader", "load", null,
            new String[]{"net.minecraft.server.packs.resources.ResourceManager", "java.util.List", "java.util.List"}, resources, base,
            m.get("net.minecraft.resources.RegistryDataLoader", "WORLDGEN_REGISTRIES"));
        var entries = (Stream<?>)call("net.minecraft.core.RegistryAccess", "registries", dynamic, new String[]{});
        entries.forEach(entry -> base.add(call("net.minecraft.core.RegistryAccess$RegistryEntry", "value", entry, new String[]{})));
        access = VanillaDumper.Mojang.create(m.constructor("net.minecraft.core.RegistryAccess$ImmutableRegistryAccess", "java.util.List"), base);
        var ops = (DynamicOps<Object>)call("net.minecraft.resources.RegistryOps", "create", null,
            new String[]{"com.mojang.serialization.DynamicOps", "net.minecraft.core.HolderLookup$Provider"}, JsonOps.INSTANCE, access);

        var serializers = "net.minecraft.network.syncher.EntityDataSerializers";
        var block = call("net.minecraft.world.level.block.Block", "defaultBlockState", m.get("net.minecraft.world.level.block.Blocks", "STONE"), new String[]{});
        var position = VanillaDumper.Mojang.create(m.constructor("net.minecraft.core.BlockPos", "int", "int", "int"), -4, 72, 9);
        var component = call("net.minecraft.network.chat.Component", "literal", null, new String[]{"java.lang.String"}, "Fixture");
        var itemCodec = (Codec<Object>)m.get("net.minecraft.world.item.ItemStack", "CODEC");
        var item = itemCodec.parse(ops, JsonParser.parseString("{\"id\":\"minecraft:diamond\",\"count\":3}")).getOrThrow();
        var particleCodec = (Codec<Object>)m.get("net.minecraft.core.particles.ParticleTypes", "CODEC");
        var particles = new ArrayList<Object>();
        for (var input : List.of(
            "{\"type\":\"minecraft:block\",\"block_state\":{\"Name\":\"minecraft:stone\"}}",
            "{\"type\":\"minecraft:dust\",\"color\":16711680,\"scale\":1.5}",
            "{\"type\":\"minecraft:dust_color_transition\",\"from_color\":16711680,\"to_color\":255,\"scale\":0.5}",
            "{\"type\":\"minecraft:effect\",\"color\":-1,\"power\":0.75}",
            "{\"type\":\"minecraft:entity_effect\",\"color\":-16711936}",
            "{\"type\":\"minecraft:item\",\"item\":{\"id\":\"minecraft:diamond\"}}",
            "{\"type\":\"minecraft:sculk_charge\",\"roll\":0.25}",
            "{\"type\":\"minecraft:shriek\",\"delay\":17}",
            "{\"type\":\"minecraft:trail\",\"target\":[1,2,3],\"color\":255,\"duration\":20}",
            "{\"type\":\"minecraft:dragon_breath\",\"power\":0.5}"))
            particles.add(particleCodec.parse(ops, JsonParser.parseString(input)).getOrThrow());
        var output = new TreeMap<Integer, String>();
        var combined = Unpooled.buffer();
        for (var f : m.type(serializers).getDeclaredFields()) {
            if (!m.type("net.minecraft.network.syncher.EntityDataSerializer").isAssignableFrom(f.getType())) continue;
            var serializer = VanillaDumper.Mojang.getStatic(f);
            int id = (int)call(serializers, "getSerializedId", null, new String[]{"net.minecraft.network.syncher.EntityDataSerializer"}, serializer);
            String name = m.fieldName(m.type(serializers), f);
            Object value = switch(name) {
                case "BYTE" -> (byte)33;
                case "INT" -> 300;
                case "LONG" -> -1L;
                case "FLOAT" -> 1.25f;
                case "STRING" -> "hello";
                case "COMPONENT" -> component;
                case "OPTIONAL_COMPONENT" -> Optional.of(component);
                case "ITEM_STACK" -> item;
                case "BOOLEAN" -> true;
                case "ROTATIONS" -> VanillaDumper.Mojang.create(m.constructor("net.minecraft.core.Rotations", "float", "float", "float"), 10f, 20f, 30f);
                case "BLOCK_POS" -> position;
                case "OPTIONAL_BLOCK_POS" -> Optional.of(position);
                case "DIRECTION" -> m.get("net.minecraft.core.Direction", "EAST");
                case "OPTIONAL_LIVING_ENTITY_REFERENCE" -> Optional.of(call("net.minecraft.world.entity.EntityReference", "of", null, new String[]{"java.util.UUID"}, UUID.fromString("00112233-4455-6677-8899-aabbccddeeff")));
                case "BLOCK_STATE" -> block;
                case "OPTIONAL_BLOCK_STATE" -> Optional.of(block);
                case "PARTICLE" -> particles.get(1);
                case "PARTICLES" -> particles;
                case "VILLAGER_DATA" -> VanillaDumper.Mojang.create(m.constructor("net.minecraft.world.entity.npc.villager.VillagerData", "net.minecraft.core.Holder", "net.minecraft.core.Holder", "int"), holder("VILLAGER_TYPE"), holder("VILLAGER_PROFESSION"), 3);
                case "OPTIONAL_UNSIGNED_INT" -> OptionalInt.of(42);
                case "POSE" -> m.get("net.minecraft.world.entity.Pose", "SWIMMING");
                case "CAT_VARIANT", "COW_VARIANT", "WOLF_VARIANT", "WOLF_SOUND_VARIANT", "FROG_VARIANT", "PIG_VARIANT", "CHICKEN_VARIANT", "ZOMBIE_NAUTILUS_VARIANT", "PAINTING_VARIANT" -> holder(name);
                case "OPTIONAL_GLOBAL_POS" -> Optional.of(call("net.minecraft.core.GlobalPos", "of", null, new String[]{"net.minecraft.resources.ResourceKey", "net.minecraft.core.BlockPos"}, m.get("net.minecraft.world.level.Level", "OVERWORLD"), position));
                case "SNIFFER_STATE" -> m.get("net.minecraft.world.entity.animal.sniffer.Sniffer$State", "DIGGING");
                case "ARMADILLO_STATE" -> m.get("net.minecraft.world.entity.animal.armadillo.Armadillo$ArmadilloState", "SCARED");
                case "COPPER_GOLEM_STATE" -> m.get("net.minecraft.world.entity.animal.golem.CopperGolemState", "IDLE");
                case "WEATHERING_COPPER_STATE" -> m.get("net.minecraft.world.level.block.WeatheringCopper$WeatherState", "OXIDIZED");
                case "VECTOR3" -> new org.joml.Vector3f(1,2,3);
                case "QUATERNION" -> new org.joml.Quaternionf(0,0,0,1);
                case "RESOLVABLE_PROFILE" -> call("net.minecraft.world.item.component.ResolvableProfile", "createUnresolved", null, new String[]{"java.lang.String"}, "Fixture");
                case "HUMANOID_ARM" -> m.get("net.minecraft.world.entity.HumanoidArm", "LEFT");
                default -> throw new IllegalStateException(name);
            };
            var buf = Unpooled.buffer();
            var friendly = VanillaDumper.Mojang.create(m.constructor("net.minecraft.network.RegistryFriendlyByteBuf", "io.netty.buffer.ByteBuf", "net.minecraft.core.RegistryAccess"), buf, access);
            var codec = call("net.minecraft.network.syncher.EntityDataSerializer", "codec", serializer, new String[]{});
            call("net.minecraft.network.codec.StreamEncoder", "encode", codec, new String[]{"java.lang.Object", "java.lang.Object"}, friendly, value);
            output.put(id, ByteBufUtil.hexDump(buf));
            combined.writeByte(id); combined.writeByte(id); combined.writeBytes(buf); buf.release();
        }
        combined.writeByte(255);
        var result = new LinkedHashMap<String,Object>(); result.put("serializers", output); result.put("metadata",ByteBufUtil.hexDump(combined)); combined.release();
        Files.writeString(Path.of(args[1]), new GsonBuilder().setPrettyPrinting().create().toJson(result));
        System.out.println("Captured " + output.size() + " metadata serializers.");
    }
}
