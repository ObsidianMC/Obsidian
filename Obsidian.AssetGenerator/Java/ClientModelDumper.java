import java.nio.file.Path;
import java.util.*;

/** Captures evaluated layer geometry, not client source. Also includes block-entity layers for later renderers. */
class ClientModelDumper {
    static final String PART = "net.minecraft.client.model.geom.ModelPart";
    static Object field(String owner, String name, Object target) {
        return Vanilla.get(Vanilla.field(owner, name), target);
    }
    static Object call(String owner, String name, Object target) {
        return Vanilla.call(Vanilla.method(owner, name), target);
    }
    public static void main(String[] args) throws Exception {
        call("net.minecraft.SharedConstants", "tryDetectVersion", null);
        call("net.minecraft.server.Bootstrap", "bootStrap", null);
        var roots = (Map<?, ?>) call("net.minecraft.client.model.geom.LayerDefinitions", "createRoots", null);
        var layers = new TreeMap<String, Object>();
        for (var entry : roots.entrySet()) {
            var layer = entry.getValue();
            var material = field("net.minecraft.client.model.geom.builders.LayerDefinition", "material", layer);
            var root = call("net.minecraft.client.model.geom.builders.LayerDefinition", "bakeRoot", layer);
            layers.put(entry.getKey().toString(), Map.of("width", field("net.minecraft.client.model.geom.builders.MaterialDefinition", "xTexSize", material),
                "height", field("net.minecraft.client.model.geom.builders.MaterialDefinition", "yTexSize", material), "root", part(root)));
        }
        var serializers = new TreeMap<String, Object>();
        var owner = "net.minecraft.network.syncher.EntityDataSerializers";
        var serializerType = Vanilla.type("net.minecraft.network.syncher.EntityDataSerializer");
        for (var f : Vanilla.type(owner).getDeclaredFields()) {
            if (!serializerType.isAssignableFrom(f.getType())) continue;
            var value = Vanilla.getStatic(f);
            serializers.put(f.getName(), Vanilla.call(
                Vanilla.method(owner, "getSerializedId", "net.minecraft.network.syncher.EntityDataSerializer"), null, value));
        }
        VanillaDumper.Json.write(Path.of(args[0]), Map.of("version", 1, "layers", layers, "serializers", serializers,
            "animations", animations()), false);
        System.out.println("Captured " + layers.size() + " client model layers.");
    }
    /** Animation keyframes are evaluated data, like the baked vertices above. No client source is exported. */
    static Object animations() {
        var result = new TreeMap<String, Object>();
        var definition = "net.minecraft.client.animation.AnimationDefinition";
        var channelType = "net.minecraft.client.animation.AnimationChannel";
        var keyType = "net.minecraft.client.animation.Keyframe";
        for (var owner : Vanilla.classesIn("net.minecraft.client.animation.definitions")) {
            for (var f : Vanilla.type(owner).getDeclaredFields()) {
                if (f.getType() != Vanilla.type(definition)) continue;
                var animation = Vanilla.getStatic(f);
                var channels = new ArrayList<Object>();
                for (var bone : ((Map<?, ?>)call(definition, "boneAnimations", animation)).entrySet()) {
                    for (var channel : (List<?>)bone.getValue()) {
                        var target = call(channelType, "target", channel);
                        var targetName = "";
                        for (var name : List.of("POSITION", "ROTATION", "SCALE"))
                            if (target == field(channelType + "$Targets", name, null)) targetName = name;
                        if (targetName.isEmpty()) throw new IllegalStateException("Unknown animation target");
                        var keys = new ArrayList<Object>();
                        for (var key : (Object[])call(channelType, "keyframes", channel)) {
                            var interpolation = call(keyType, "interpolation", key);
                            var cubic = interpolation == field(channelType + "$Interpolations", "CATMULLROM", null);
                            if (!cubic && interpolation != field(channelType + "$Interpolations", "LINEAR", null))
                                throw new IllegalStateException("Unknown keyframe interpolation");
                            keys.add(Map.of("time", call(keyType, "timestamp", key), "pre", vector(call(keyType, "preTarget", key)),
                                "post", vector(call(keyType, "postTarget", key)), "cubic", cubic));
                        }
                        channels.add(Map.of("bone", bone.getKey(), "target", targetName, "keys", keys));
                    }
                }
                result.put(Vanilla.simpleName(Vanilla.type(owner)) + "/" + f.getName(),
                    Map.of("length", call(definition, "lengthInSeconds", animation), "loop", call(definition, "looping", animation), "channels", channels));
            }
        }
        return result;
    }
    static Object vector(Object value) {
        var vector = (org.joml.Vector3fc)value;
        return List.of(vector.x(), vector.y(), vector.z());
    }
    static Object part(Object part) {
        var result = new LinkedHashMap<String, Object>();
        for (var name : List.of("x", "y", "z", "xRot", "yRot", "zRot", "xScale", "yScale", "zScale"))
            result.put(name, field(PART, name, part));
        var cubes = new ArrayList<Object>();
        for (var cube : (List<?>) field(PART, "cubes", part)) {
            var faces = new ArrayList<Object>();
            for (var polygon : (Object[]) field(PART + "$Cube", "polygons", cube)) {
                var vertices = new ArrayList<Object>();
                for (var vertex : (Object[]) field(PART + "$Polygon", "vertices", polygon)) {
                    var values = new ArrayList<Object>();
                    for (var name : List.of("x", "y", "z", "u", "v")) {
                        var number = ((Number)field(PART + "$Vertex", name, vertex)).floatValue();
                        // The untextured boat water mask has a 0x0 material and undefined UVs.
                        values.add(Float.isFinite(number) ? number : 0f);
                    }
                    vertices.add(values);
                }
                faces.add(vertices);
            }
            cubes.add(faces);
        }
        result.put("cubes", cubes);
        var children = new TreeMap<String, Object>();
        for (var child : ((Map<?, ?>) field(PART, "children", part)).entrySet())
            children.put(child.getKey().toString(), part(child.getValue()));
        result.put("children", children);
        return result;
    }
}
