using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    /// <summary>
    /// Saved data with the level's state only Obsidian keeps, in its folder's <c>data/obsidian/</c>, which vanilla
    /// ignores: raids, patrols, the wandering trader's countdown, what vanilla's dragon fight doesn't keep, and a
    /// dimension's spawn and time.
    /// </summary>
    internal const string StateDataId = "obsidian:level_state";

    private const string EndFightDataId = "minecraft:ender_dragon_fight";

    // Vanilla's dragon fight as it was read, so what Obsidian doesn't model of it survives.
    private NbtCompound? vanillaEndFight;

    /// <summary>
    /// The file of the level's state only Obsidian keeps (see <see cref="StateDataId"/>).
    /// </summary>
    internal string StatePath => WorldLayout.SavedDataPath(this.FolderPath, StateDataId);

    /// <summary>
    /// Reads the level's saved state: Obsidian's (see <see cref="StateDataId"/>), then vanilla's dragon fight, which wins
    /// for what both keep.
    /// </summary>
    protected void ReadLevelState()
    {
        if (SavedDataFile.Read(this.StatePath) is NbtCompound state)
        {
            this.ReadRaidsNbt(state);
            this.ReadEndFightNbt(state);
            this.ReadOwnState(state);
        }

        if (this.HasEndFight && SavedDataFile.Read(WorldLayout.SavedDataPath(this.FolderPath, EndFightDataId)) is NbtCompound fight)
        {
            this.vanillaEndFight = fight;
            this.ReadVanillaEndFight(fight);
        }
    }

    /// <summary>
    /// Writes the level's state (see <see cref="ReadLevelState"/>) and its plugins' saved data.
    /// </summary>
    internal void SaveLevelState()
    {
        using (var writer = new RawNbtWriter("data"))
        {
            this.WriteRaidsNbt(writer);
            this.WriteEndFightNbt(writer);
            this.WriteOwnState(writer);
            writer.EndCompound();

            SavedDataFile.Write(this.StatePath, (NbtCompound)NbtReader.ReadTag(writer.Data, true, out _)!);
        }

        if (this.HasEndFight)
        {
            var fight = this.vanillaEndFight ??= new NbtCompound("data");
            this.WriteVanillaEndFight(fight);
            SavedDataFile.Write(WorldLayout.SavedDataPath(this.FolderPath, EndFightDataId), fight);
        }

        this.SavePluginData();
    }

    /// <summary>
    /// Reads what a kind of level keeps of its own in its state (see <see cref="StateDataId"/>).
    /// </summary>
    protected virtual void ReadOwnState(NbtCompound state)
    {
    }

    /// <summary>
    /// Writes what a kind of level keeps of its own in its state (see <see cref="StateDataId"/>).
    /// </summary>
    protected virtual void WriteOwnState(INbtWriter writer)
    {
    }
}
