import com.google.gson.*;
import java.nio.file.*;
import java.util.*;

/** Captures the movement and interaction packets' bytes from synthetic inputs, which MovementPayloads replays. */
class MovementFixtures extends ScreenFixtures {
    static final String ENTITY_PKG = "net.minecraft.world.entity.";
    static final String VEC3 = "net.minecraft.world.phys.Vec3";
    static final String HAND = "net.minecraft.world.InteractionHand";
    static final String SLOT = "net.minecraft.world.level.block.entity.SignTextSlot";
    static final String DIRECTION = "net.minecraft.core.Direction";
    static Object vec(double x, double y, double z) {
        return make(VEC3, new String[]{"double", "double", "double"}, x, y, z);
    }

    static Object constant(String owner, String name) {
        return Vanilla.get(owner, name);
    }

    static Object linear(int x, int y, int z) {
        return make(GAME + "VecDelta$Linear", new String[]{"short", "short", "short"}, (short) x, (short) y, (short) z);
    }

    static Object change(Object position, Object delta, float yRot, float xRot) {
        return make(ENTITY_PKG + "PositionMoveRotation", new String[]{VEC3, VEC3, "float", "float"},
            position, delta, yRot, xRot);
    }

    public static void main(String[] args) throws Exception {
        call("net.minecraft.SharedConstants", "tryDetectVersion", null, new String[]{});
        call("net.minecraft.server.Bootstrap", "bootStrap", null, new String[]{});
        var builtin = Vanilla.get("net.minecraft.core.registries.BuiltInRegistries", "REGISTRY");
        access = call("net.minecraft.core.RegistryAccess", "fromRegistryOfRegistries", null,
            new String[]{"net.minecraft.core.Registry"}, builtin);

        var output = new JsonObject();
        clientbound(output);
        serverbound(output);
        Files.writeString(Path.of(args[0]), new GsonBuilder().setPrettyPrinting().create().toJson(output));
    }

    /** Clientbound entity movement. */
    static void clientbound(JsonObject output) throws Exception {
        var delta = GAME + "VecDelta";
        packet(output, "move_entity_pos", GAME + "ClientboundMoveEntityPacket$Pos",
            new String[]{"int", delta, "boolean"},
            301, linear(1234, -56, 32767), true);
        packet(output, "move_entity_pos_rot", GAME + "ClientboundMoveEntityPacket$PosRot",
            new String[]{"int", delta, "byte", "byte", "boolean"},
            302, linear(-32768, 0, 17), (byte) 64, (byte) 16, false);
        packet(output, "move_entity_rot", GAME + "ClientboundMoveEntityPacket$Rot",
            new String[]{"int", "byte", "byte", "boolean"},
            303, (byte) -128, (byte) 16, true);

        var path = call(ENTITY_PKG + "PositionPath", "of", null, new String[]{VEC3}, vec(-1024.25, 70.5, 3e6 + 0.125));
        packet(output, "entity_position_sync", GAME + "ClientboundEntityPositionSyncPacket",
            new String[]{"int", ENTITY_PKG + "PositionPath", "float", "float", "boolean"},
            304, path, 90f, 22.5f, true);

        var teleport = change(vec(1.5, -64, 2.25), vec(0.5, -0.25, 0), 90f, 22.5f);
        packet(output, "teleport_entity", GAME + "ClientboundTeleportEntityPacket",
            new String[]{"int", ENTITY_PKG + "PositionMoveRotation", "java.util.Set", "boolean"},
            305, teleport, Set.of(), false);
        packet(output, "set_entity_motion", GAME + "ClientboundSetEntityMotionPacket",
            new String[]{"int", VEC3},
            306, vec(0.25, -0.5, 1.75));

        var swing = "net.minecraft.world.item.component.SwingAnimation";
        var swingType = "net.minecraft.world.item.SwingAnimationType";
        var stab = make(swing, new String[]{swingType, "int"}, constant(swingType, "STAB"), 19);
        packet(output, "swing_animation", GAME + "ClientboundSwingAnimationPacket",
            new String[]{"int", HAND, swing},
            307, constant(HAND, "OFF_HAND"), stab);

        var unsafeField = sun.misc.Unsafe.class.getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = (sun.misc.Unsafe) unsafeField.get(null);
        var sleeper = unsafe.allocateInstance(Vanilla.type("net.minecraft.world.entity.decoration.ArmorStand"));
        Vanilla.field("net.minecraft.world.entity.Entity", "id").setInt(sleeper, 308);
        packet(output, "animate_wake_up", GAME + "ClientboundAnimatePacket",
            new String[]{"net.minecraft.world.entity.Entity", "int"},
            sleeper, Vanilla.get(GAME + "ClientboundAnimatePacket", "WAKE_UP"));
        packet(output, "open_sign_editor", GAME + "ClientboundOpenSignEditorPacket",
            new String[]{POS, SLOT},
            pos(-12, 70, 23), constant(SLOT, "BACK"));
    }

    /** Serverbound movement and interaction. */
    static void serverbound(JsonObject output) {
        packet(output, "accept_teleportation", GAME + "ServerboundAcceptTeleportationPacket",
            new String[]{"int", "double", "double", "double", "float", "float"},
            417, -12.5, 64.0, 1e7 + 0.75, -170.5f, 45.25f);
        packet(output, "attack", GAME + "ServerboundAttackPacket", new String[]{"int"}, 300000);
        packet(output, "interact", GAME + "ServerboundInteractPacket",
            new String[]{"int", HAND, VEC3, "boolean"},
            309, constant(HAND, "OFF_HAND"), vec(0.25, 1.5, -0.375), true);

        var punch = Vanilla.get(GAME + "ServerboundPunchPacket", "INSTANCE");
        output.addProperty("punch", encode(GAME + "ServerboundPunchPacket", "STREAM_CODEC", punch));
        packet(output, "spectator_action", GAME + "ServerboundSpectatorActionPacket",
            new String[]{"java.util.OptionalInt"},
            OptionalInt.of(310));
        packet(output, "spectator_action_none", GAME + "ServerboundSpectatorActionPacket",
            new String[]{"java.util.OptionalInt"},
            OptionalInt.empty());
        packet(output, "sign_update", GAME + "ServerboundSignUpdatePacket",
            new String[]{POS, "java.util.List", SLOT},
            pos(5, -60, -7), List.of("Hello", "", "世界", "line four"), constant(SLOT, "FRONT"));
        packet(output, "pick_item_from_block", GAME + "ServerboundPickItemFromBlockPacket",
            new String[]{POS, "boolean"},
            pos(-30000, 319, 29999), true);
        packet(output, "move_player_pos_rot", GAME + "ServerboundMovePlayerPacket$PosRot",
            new String[]{"double", "double", "double", "float", "float", "boolean", "boolean"},
            0.5, 65.0, -0.5, 359.5f, -90f, true, true);
        packet(output, "move_player_rot", GAME + "ServerboundMovePlayerPacket$Rot",
            new String[]{"float", "float", "boolean", "boolean"},
            -45f, 12.5f, false, true);

        var hit = make("net.minecraft.world.phys.BlockHitResult",
            new String[]{VEC3, DIRECTION, POS, "boolean", "boolean"},
            vec(4.5, 70.25, -3.75), constant(DIRECTION, "WEST"), pos(4, 70, -4), true, false);
        packet(output, "use_item_on", GAME + "ServerboundUseItemOnPacket",
            new String[]{HAND, "net.minecraft.world.phys.BlockHitResult", "int"},
            constant(HAND, "MAIN_HAND"), hit, 42);
        packet(output, "use_item", GAME + "ServerboundUseItemPacket",
            new String[]{HAND, "int", "float", "float"},
            constant(HAND, "OFF_HAND"), 43, 12.5f, -30f);

        var action = GAME + "ServerboundPlayerActionPacket$Action";
        packet(output, "player_action", GAME + "ServerboundPlayerActionPacket",
            new String[]{action, POS, DIRECTION, "int"},
            constant(action, "START_DESTROY_BLOCK"), pos(1, 2, 3), constant(DIRECTION, "UP"), 44);
    }
}
