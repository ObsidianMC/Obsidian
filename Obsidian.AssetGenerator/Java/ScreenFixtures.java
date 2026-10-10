import com.google.gson.*;
import com.mojang.serialization.*;
import com.mojang.datafixers.util.Either;
import io.netty.buffer.*;
import java.nio.file.*;
import java.util.*;

/** Builds synthetic screen inputs and captures the server's real codecs and statistic formatters. */
class ScreenFixtures {
    static VanillaDumper.Mojang m;
    static Object access;
    static final String GAME = "net.minecraft.network.protocol.game.";
    static final String COMMON = "net.minecraft.network.protocol.common.";
    static final String POS = "net.minecraft.core.BlockPos";
    static final String ID = "net.minecraft.resources.Identifier";
    static final String ENTITY = "net.minecraft.world.level.block.entity.";
    static final String BLOCK = "net.minecraft.world.level.block.";
    static final String COMPONENT = "net.minecraft.network.chat.Component";
    static Object call(String owner, String method, Object target, String[] parameters, Object... values) {
        return VanillaDumper.Mojang.call(m.method(owner, method, parameters), target, values);
    }
    static Object make(String owner, String[] parameters, Object... values) {
        return VanillaDumper.Mojang.create(m.constructor(owner, parameters), values);
    }
    static Object id(String value) { return call(ID, "parse", null, new String[]{"java.lang.String"}, value); }
    static Object pos(int x, int y, int z) { return make(POS, new String[]{"int","int","int"}, x,y,z); }
    static String encode(String owner, String field, Object value) {
        var bytes = Unpooled.buffer();
        try {
            var buffer = make("net.minecraft.network.RegistryFriendlyByteBuf", new String[]{"io.netty.buffer.ByteBuf","net.minecraft.core.RegistryAccess"}, bytes, access);
            call("net.minecraft.network.codec.StreamEncoder", "encode", m.get(owner, field), new String[]{"java.lang.Object","java.lang.Object"}, buffer, value);
            return ByteBufUtil.hexDump(bytes);
        } finally { bytes.release(); }
    }
    static void packet(JsonObject output, String name, String owner, String[] types, Object... values) {
        output.addProperty(name, encode(owner, "STREAM_CODEC", make(owner, types, values)));
    }
    public static void main(String[] args) throws Exception {
        Locale.setDefault(Locale.US);
        m = VanillaDumper.Mojang.read(Path.of(args[0]));
        call("net.minecraft.SharedConstants", "tryDetectVersion", null, new String[]{});
        call("net.minecraft.server.Bootstrap", "bootStrap", null, new String[]{});
        access = call("net.minecraft.core.RegistryAccess", "fromRegistryOfRegistries", null, new String[]{"net.minecraft.core.Registry"}, m.get("net.minecraft.core.registries.BuiltInRegistries", "REGISTRY"));
        var output = new JsonObject();
        var position = pos(-12,70,23);
        packet(output, "command_block", GAME+"ServerboundSetCommandBlockPacket",
            new String[]{POS,"java.lang.String",ENTITY+"CommandBlockEntity$Mode","boolean","boolean","boolean"},
            position,"say fixture",m.get(ENTITY+"CommandBlockEntity$Mode","AUTO"),true,true,true);
        packet(output, "command_block_minecart", GAME+"ServerboundSetCommandMinecartPacket",
            new String[]{"int","java.lang.String","boolean"},301,"say cart",false);
        packet(output, "structure_block", GAME+"ServerboundSetStructureBlockPacket",
            new String[]{POS,ENTITY+"StructureBlockEntity$UpdateType",BLOCK+"state.properties.StructureMode","java.lang.String",POS,"net.minecraft.core.Vec3i",BLOCK+"Mirror",BLOCK+"Rotation","java.lang.String","boolean","boolean","boolean","boolean","float","long"},
            position,m.get(ENTITY+"StructureBlockEntity$UpdateType","LOAD_AREA"),m.get(BLOCK+"state.properties.StructureMode","LOAD"),"minecraft:fixture",pos(-3,1,2),pos(10,11,12),m.get(BLOCK+"Mirror","FRONT_BACK"),m.get(BLOCK+"Rotation","COUNTERCLOCKWISE_90"),"data",true,true,false,true,.75f,-123L);
        packet(output, "jigsaw", GAME+"ServerboundSetJigsawBlockPacket",
            new String[]{POS,ID,ID,ID,"java.lang.String",ENTITY+"JigsawBlockEntity$JointType","int","int"},
            position,id("fixture:start"),id("fixture:target"),id("fixture:pool"),"minecraft:air",m.get(ENTITY+"JigsawBlockEntity$JointType","ROLLABLE"),-2,3);
        packet(output, "test_block", GAME+"ServerboundSetTestBlockPacket",
            new String[]{POS,BLOCK+"state.properties.TestBlockMode","java.lang.String"},position,m.get(BLOCK+"state.properties.TestBlockMode","FAIL"),"test failed");
        packet(output, "test_instance_block", GAME+"ServerboundTestInstanceBlockActionPacket",
            new String[]{POS,GAME+"ServerboundTestInstanceBlockActionPacket$Action","java.util.Optional","net.minecraft.core.Vec3i",BLOCK+"Rotation","boolean"},
            position,m.get(GAME+"ServerboundTestInstanceBlockActionPacket$Action","RUN"),Optional.of(call("net.minecraft.resources.ResourceKey","create",null,new String[]{"net.minecraft.resources.ResourceKey",ID},m.get("net.minecraft.core.registries.Registries","TEST_INSTANCE"),id("fixture:test"))),pos(4,5,6),m.get(BLOCK+"Rotation","CLOCKWISE_90"),true);
        var tag = make("net.minecraft.nbt.CompoundTag", new String[]{});
        call("net.minecraft.nbt.CompoundTag","putString",tag,new String[]{"java.lang.String","java.lang.String"},"text","A\0\ud83d\ude00");
        call("net.minecraft.nbt.CompoundTag","putBoolean",tag,new String[]{"java.lang.String","boolean"},"ready",true);
        call("net.minecraft.nbt.CompoundTag","putFloat",tag,new String[]{"java.lang.String","float"},"amount",1.5f);
        packet(output,"custom_click",COMMON+"ServerboundCustomClickActionPacket",new String[]{ID,"java.util.Optional"},id("fixture:action"),Optional.of(tag));
        // Initialize the enclosing codecs before constructing a nested entry.
        m.get("net.minecraft.server.ServerLinks", "EMPTY");
        var entry = "net.minecraft.server.ServerLinks$UntrustedEntry";
        var known = make(entry,new String[]{"com.mojang.datafixers.util.Either","java.lang.String"},Either.left(m.get("net.minecraft.server.ServerLinks$KnownLinkType","WEBSITE")),"https://www.minecraft.net");
        var custom = make(entry,new String[]{"com.mojang.datafixers.util.Either","java.lang.String"},Either.right(call(COMPONENT,"literal",null,new String[]{"java.lang.String"},"Community rules")),"https://www.minecraft.net/community-standards");
        packet(output,"server_links",COMMON+"ClientboundServerLinksPacket",new String[]{"java.util.List"},List.of(known,custom));
        var ops = (DynamicOps<Object>)call("net.minecraft.resources.RegistryOps","create",null,new String[]{"com.mojang.serialization.DynamicOps","net.minecraft.core.HolderLookup$Provider"},JsonOps.INSTANCE,access);
        var codec = (Codec<Object>)m.get("net.minecraft.server.dialog.Dialog","DIRECT_CODEC");
        var dialog = codec.parse(ops,JsonParser.parseString("{\"type\":\"minecraft:notice\",\"title\":\"Fixture notice\",\"body\":{\"type\":\"minecraft:plain_message\",\"contents\":\"Fixture body\"}}" )).getOrThrow();
        var holder = call("net.minecraft.core.Holder","direct",null,new String[]{"java.lang.Object"},dialog);
        var packet = make(COMMON+"ClientboundShowDialogPacket",new String[]{"net.minecraft.core.Holder"},holder);
        output.addProperty("play_dialog",encode(COMMON+"ClientboundShowDialogPacket","STREAM_CODEC",packet));
        output.addProperty("configuration_dialog",encode(COMMON+"ClientboundShowDialogPacket","CONTEXT_FREE_STREAM_CODEC",packet));
        var statistics = new JsonArray();
        for (var name : List.of("DEFAULT","DISTANCE","TIME","DIVIDE_BY_TEN"))
            for (int value : new int[]{0,1,50,51,600,601,50000,50001,36000,36001,864000,864001,1234567}) {
                var row = new JsonArray();row.add(name);row.add(value);
                row.add((String)call("net.minecraft.stats.StatFormatter","format",m.get("net.minecraft.stats.StatFormatter",name),new String[]{"int"},value));statistics.add(row);
            }
        output.add("statistics",statistics);
        Files.writeString(Path.of(args[1]),new GsonBuilder().setPrettyPrinting().create().toJson(output));
    }
}
