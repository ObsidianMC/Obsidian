import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import java.nio.file.Path;
import net.minecraft.network.syncher.EntityDataAccessor;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.level.GameType;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;

/** Records shared entity flags after native effect reconciliation and game mode changes on vanilla 26.3. */
public final class InvisibilityFixtures {
    public static void main(String[] args) {
        try {
            ArmorStandFixtures.captureInvisibility(Path.of(args[0]));
            System.out.println("Wrote " + args[0]);
            System.out.flush();
            Runtime.getRuntime().halt(0);
        } catch (Throwable error) {
            error.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }
    }

    static JsonObject capture(MinecraftServer server, ServerPlayer player) throws Exception {
        var root = new JsonObject();
        root.addProperty("version", server.getServerVersion());
        root.addProperty("notes", "Native updateInvisibilityStatus after each effect mutation; "
            + "setGameMode for spectator transitions; metadata index 0 shared flag byte.");
        var cases = new JsonArray();
        root.add("cases", cases);

        var pig = create(server, "pig");
        var effects = scenario(cases, "effect_add_replace_remove_expire", "pig", false);
        step(effects, pig, "initial");
        pig.addEffect(new MobEffectInstance(MobEffects.INVISIBILITY, 200));
        step(effects, pig, "add");
        pig.addEffect(new MobEffectInstance(MobEffects.INVISIBILITY, 2, 1));
        step(effects, pig, "replace");
        pig.removeEffect(MobEffects.INVISIBILITY);
        step(effects, pig, "remove");
        pig.addEffect(new MobEffectInstance(MobEffects.INVISIBILITY, 1));
        step(effects, pig, "add_short");
        var tick = LivingEntity.class.getDeclaredMethod("tickEffects");
        tick.setAccessible(true);
        tick.invoke(pig);
        step(effects, pig, "expire");

        var modes = scenario(cases, "spectator_and_effect", "player", false);
        player.setGameMode(GameType.SURVIVAL);
        step(modes, player, "initial");
        player.setGameMode(GameType.SPECTATOR);
        step(modes, player, "spectator");
        player.setGameMode(GameType.SURVIVAL);
        step(modes, player, "survival");
        player.setGameMode(GameType.SPECTATOR);
        step(modes, player, "spectator");
        player.addEffect(new MobEffectInstance(MobEffects.INVISIBILITY, 200));
        step(modes, player, "add");
        player.removeEffect(MobEffects.INVISIBILITY);
        step(modes, player, "remove");
        player.addEffect(new MobEffectInstance(MobEffects.INVISIBILITY, 200));
        step(modes, player, "add");
        player.setGameMode(GameType.SURVIVAL);
        step(modes, player, "survival");
        player.removeEffect(MobEffects.INVISIBILITY);
        step(modes, player, "remove");

        for (boolean invisible : new boolean[]{false, true}) {
            var stand = create(server, "armor_stand");
            stand.setInvisible(invisible);
            var flags = scenario(cases, "armor_stand_" + invisible, "armor_stand", invisible);
            step(flags, stand, "initial");
            stand.addEffect(new MobEffectInstance(MobEffects.INVISIBILITY, 200));
            step(flags, stand, "add");
            stand.removeEffect(MobEffects.INVISIBILITY);
            step(flags, stand, "remove");
        }
        return root;
    }

    private static LivingEntity create(MinecraftServer server, String name) {
        var type = BuiltInRegistries.ENTITY_TYPE.getValue(Identifier.parse("minecraft:" + name));
        return (LivingEntity) type.create(server.overworld(), EntitySpawnReason.COMMAND);
    }

    private static JsonArray scenario(JsonArray cases, String name, String kind, boolean invisible) {
        var scenario = new JsonObject();
        scenario.addProperty("name", name);
        scenario.addProperty("entity", kind);
        scenario.addProperty("invisible", invisible);
        var steps = new JsonArray();
        scenario.add("steps", steps);
        cases.add(scenario);
        return steps;
    }

    @SuppressWarnings("unchecked")
    private static void step(JsonArray steps, LivingEntity entity, String action) throws Exception {
        // Invoke the native virtual routine that the dirty-effect tick calls, without unrelated entity AI.
        var refresh = LivingEntity.class.getDeclaredMethod("updateInvisibilityStatus");
        refresh.setAccessible(true);
        refresh.invoke(entity);
        var field = Entity.class.getDeclaredField("DATA_SHARED_FLAGS_ID");
        field.setAccessible(true);
        var accessor = (EntityDataAccessor<Byte>) field.get(null);
        var output = new JsonObject();
        output.addProperty("action", action);
        output.addProperty("flags", Byte.toUnsignedInt(entity.getEntityData().get(accessor)));
        steps.add(output);
    }
}
