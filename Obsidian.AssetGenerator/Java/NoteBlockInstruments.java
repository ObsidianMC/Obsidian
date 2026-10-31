import com.google.gson.GsonBuilder;
import com.google.gson.JsonObject;
import java.nio.file.Files;
import java.nio.file.Path;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.level.block.state.properties.NoteBlockInstrument;

final class NoteBlockInstruments {
    static void dumpInstruments(Path output) throws Exception {
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
        var data = new JsonObject();
        data.addProperty("version", net.minecraft.SharedConstants.getCurrentVersion().name());
        var blocks = new JsonObject();
        for (var block : BuiltInRegistries.BLOCK) {
            var instrument = block.defaultBlockState().instrument();
            if (instrument != NoteBlockInstrument.HARP) {
                blocks.addProperty(BuiltInRegistries.BLOCK.getKey(block).toString(), instrument.getSerializedName());
            }
        }

        data.add("blocks", blocks);
        var instruments = new JsonObject();
        for (var instrument : NoteBlockInstrument.values()) {
            var value = new JsonObject();
            value.addProperty("sound", instrument.getSoundEvent().value().location().toString());
            value.addProperty("above", instrument.worksAboveNoteBlock());
            value.addProperty("tunable", instrument.isTunable());
            value.addProperty("custom", instrument.hasCustomSound());
            instruments.add(instrument.getSerializedName(), value);
        }

        data.add("instruments", instruments);
        Files.writeString(output, gson.toJson(data) + "\n");
    }
}
