import com.google.gson.*;
import com.mojang.brigadier.arguments.*;
import com.mojang.brigadier.builder.*;
import com.mojang.brigadier.tree.*;
import java.nio.file.*;
import java.lang.reflect.Proxy;
import java.util.*;

/** Captures the remaining small protocol/audio/noise references from synthetic inputs, rather than storing bytes in tests. */
class ClientProtocolFixtures extends ScreenFixtures {
    static Object random(long seed) { return call("net.minecraft.util.RandomSource","create",null,new String[]{"long"},seed); }
    static Object direct(Object value) { return call("net.minecraft.core.Holder","direct",null,new String[]{"java.lang.Object"},value); }
    public static void main(String[] args) throws Exception {
        m = VanillaDumper.Mojang.read(Path.of(args[0]));
        call("net.minecraft.SharedConstants","tryDetectVersion",null,new String[]{});
        call("net.minecraft.server.Bootstrap","bootStrap",null,new String[]{});
        access = call("net.minecraft.core.RegistryAccess","fromRegistryOfRegistries",null,new String[]{"net.minecraft.core.Registry"},m.get("net.minecraft.core.registries.BuiltInRegistries","REGISTRY"));
        var output = new JsonObject();
        var root = new RootCommandNode<Object>();
        var fixture = LiteralArgumentBuilder.<Object>literal("fixture");
        fixture.then(RequiredArgumentBuilder.argument("bool",BoolArgumentType.bool()));
        fixture.then(RequiredArgumentBuilder.argument("float",FloatArgumentType.floatArg(-1.5f,2.5f)));
        fixture.then(RequiredArgumentBuilder.argument("double",DoubleArgumentType.doubleArg(-2.25,8.5)));
        fixture.then(RequiredArgumentBuilder.argument("integer",IntegerArgumentType.integer(-7,42)).executes(context -> 1));
        fixture.then(RequiredArgumentBuilder.argument("long",LongArgumentType.longArg(Long.MIN_VALUE,Long.MAX_VALUE-1)));
        fixture.then(RequiredArgumentBuilder.argument("word",StringArgumentType.word()));
        fixture.then(RequiredArgumentBuilder.argument("quoted",StringArgumentType.string()));
        fixture.then(RequiredArgumentBuilder.argument("greedy",StringArgumentType.greedyString()));
        fixture.then(RequiredArgumentBuilder.argument("entity",(ArgumentType<?>)make("net.minecraft.commands.arguments.EntityArgument",new String[]{"boolean","boolean"},true,true)));
        fixture.then(RequiredArgumentBuilder.argument("time",(ArgumentType<?>)call("net.minecraft.commands.arguments.TimeArgument","time",null,new String[]{"int"},12)));
        fixture.then(RequiredArgumentBuilder.argument("registry",(ArgumentType<?>)make("net.minecraft.commands.arguments.ResourceKeyArgument",new String[]{"net.minecraft.resources.ResourceKey"},m.get("net.minecraft.core.registries.Registries","ITEM"))));
        var node=fixture.build();root.addChild(node);root.addChild(LiteralArgumentBuilder.<Object>literal("alias").redirect(node).build());
        var inspector=m.type(GAME+"ClientboundCommandsPacket$NodeInspector");
        var executable=m.method(GAME+"ClientboundCommandsPacket$NodeInspector","isExecutable","com.mojang.brigadier.tree.CommandNode");
        var inspect=Proxy.newProxyInstance(inspector.getClassLoader(),new Class<?>[]{inspector},(proxy,method,values) -> {
            var command=(CommandNode<?>)values[0];
            if(method.getReturnType()==boolean.class) return method.equals(executable) && command.getCommand()!=null;
            return command.getName().equals("registry") ? id("minecraft:ask_server") : null;
        });
        packet(output,"commands",GAME+"ClientboundCommandsPacket",new String[]{"com.mojang.brigadier.tree.RootCommandNode",GAME+"ClientboundCommandsPacket$NodeInspector"},root,inspect);
        var previous=new byte[256];for(int i=0;i<previous.length;i++)previous[i]=(byte)i;
        var signature=make("net.minecraft.network.chat.MessageSignature",new String[]{"byte[]"},previous);
        var seen=make("net.minecraft.network.chat.LastSeenMessages",new String[]{"java.util.List"},List.of(signature));
        var link=make("net.minecraft.network.chat.SignedMessageLink",new String[]{"int","java.util.UUID","java.util.UUID"},7,UUID.fromString("01234567-89ab-cdef-0123-456789abcdef"),UUID.fromString("fedcba98-7654-3210-fedc-ba9876543210"));
        var body=make("net.minecraft.network.chat.SignedMessageBody",new String[]{"java.lang.String","java.time.Instant","long","net.minecraft.network.chat.LastSeenMessages"},"Hello, 世界!",java.time.Instant.ofEpochMilli(1767000123456L),-918273645L,seen);
        var sinkType=m.type("net.minecraft.util.SignatureUpdater$Output");
        var bytes=new java.io.ByteArrayOutputStream();
        var sink=Proxy.newProxyInstance(sinkType.getClassLoader(),new Class<?>[]{sinkType},(proxy,method,values)->{bytes.write((byte[])values[0]);return null;});
        call("net.minecraft.network.chat.PlayerChatMessage","updateSignature",null,new String[]{"net.minecraft.util.SignatureUpdater$Output","net.minecraft.network.chat.SignedMessageLink","net.minecraft.network.chat.SignedMessageBody"},sink,link,body);
        output.addProperty("chatPayload",java.util.HexFormat.of().formatHex(bytes.toByteArray()));
        var sound=m.get("net.minecraft.sounds.SoundEvents","STONE_BREAK");
        // The vanilla field may be a direct SoundEvent; packets require a registry holder.
        if(!m.type("net.minecraft.core.Holder").isInstance(sound)) {
            var registry=m.get("net.minecraft.core.registries.BuiltInRegistries","SOUND_EVENT");
            sound=call("net.minecraft.core.Registry","wrapAsHolder",registry,new String[]{"java.lang.Object"},sound);
        }
        var category="net.minecraft.sounds.SoundSource";
        var block=m.get(category,"BLOCKS");
        packet(output,"soundPosition",GAME+"ClientboundSoundPacket",new String[]{"net.minecraft.core.Holder",category,"double","double","double","float","float","long"},sound,block,-12.25,64.5,31.875,2.5f,.75f,123456789L);
        var inline=direct(call("net.minecraft.sounds.SoundEvent","createFixedRangeEvent",null,new String[]{ID,"float"},id("fixture:inline"),24f));
        packet(output,"soundInline",GAME+"ClientboundSoundPacket",new String[]{"net.minecraft.core.Holder",category,"double","double","double","float","float","long"},inline,m.get(category,"UI"),1d,2d,3d,.25f,1.25f,-987654321L);
        var unsafeField=sun.misc.Unsafe.class.getDeclaredField("theUnsafe");unsafeField.setAccessible(true);var unsafe=(sun.misc.Unsafe)unsafeField.get(null);
        var entity=unsafe.allocateInstance(m.type("net.minecraft.world.entity.decoration.ArmorStand"));
        m.field("net.minecraft.world.entity.Entity","id").setInt(entity,301);
        packet(output,"soundEntity",GAME+"ClientboundSoundEntityPacket",new String[]{"net.minecraft.core.Holder",category,"net.minecraft.world.entity.Entity","float","float","long"},sound,m.get(category,"PLAYERS"),entity,.5f,1.5f,42L);
        var stops=new JsonArray();
        for(int flags=0;flags<4;flags++) stops.add(encode(GAME+"ClientboundStopSoundPacket","STREAM_CODEC",make(GAME+"ClientboundStopSoundPacket",new String[]{ID,category},(flags&2)==0?null:id("minecraft:block.stone.break"),(flags&1)==0?null:block)));
        output.add("soundStops",stops);
        var noise=make("net.minecraft.world.level.levelgen.synth.PerlinSimplexNoise",new String[]{"net.minecraft.util.RandomSource","java.util.List"},random(2345),List.of(0));
        var samples=new JsonArray();
        for(int[] point:new int[][]{{0,0},{1,1},{10,10},{-100,30},{1234,-567},{-17,-33},{200,200}}) {
            var row=new JsonArray();row.add(point[0]);row.add(point[1]);
            row.add((double)call("net.minecraft.world.level.levelgen.synth.PerlinSimplexNoise","getValue",noise,new String[]{"double","double","boolean"},point[0]*.0225,point[1]*.0225,false));samples.add(row);
        }
        output.add("swampNoise",samples);
        var music=new JsonObject();
        for(var name:List.of("GAME","MENU")) music.addProperty(name,(int)call("net.minecraft.client.sounds.MusicManager$MusicFrequency","getNextSongDelay",m.get("net.minecraft.client.sounds.MusicManager$MusicFrequency","DEFAULT"),new String[]{"net.minecraft.sounds.Music","net.minecraft.util.RandomSource"},m.get("net.minecraft.sounds.Musics",name),random(42)));
        output.add("music",music);
        Files.writeString(Path.of(args[1]),new GsonBuilder().setPrettyPrinting().create().toJson(output));
    }
}
