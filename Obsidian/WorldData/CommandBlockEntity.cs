using Obsidian.API.BlockEntities;
using Obsidian.Commands;
using Obsidian.Nbt;

namespace Obsidian.WorldData;

/// <summary>
/// A command block's block entity, like vanilla's <c>CommandBlockEntity</c>: its command (see
/// <see cref="StoredCommand"/>), whether redstone powers it, whether it's always active, and whether its condition
/// held when it was last checked. Impulse, repeating and chain command blocks share it; their block decides its mode
/// (see <see cref="CommandBlocks"/>).
/// </summary>
/// <remarks>
/// Saved as vanilla saves it: the command's fields, then <c>powered</c>, <c>conditionMet</c> and <c>auto</c>. Clients
/// get nothing with the chunk; an operator opening the block's editor is sent <see cref="EditorData"/>. Its fields
/// change under the level's tick lock (redstone, edits) or on its tick's run, and it's saved as it is whenever its
/// chunk is, from any thread: a save racing a run may take some fields from before it and some from after, which the
/// run's own change then saves together.
/// </remarks>
internal sealed class CommandBlockEntity : BlockEntity
{
    /// <summary>The block entity type's id.</summary>
    public const string TypeId = "minecraft:command_block";

    /// <summary>The command and what its last run did.</summary>
    public StoredCommand Command { get; } = new();

    /// <summary>Whether a redstone signal reaches the block (vanilla's <c>powered</c>).</summary>
    public bool Powered { get; set; }

    /// <summary>Whether it runs without redstone, its editor's "Always Active" (vanilla's <c>auto</c>).</summary>
    public bool Automatic { get; set; }

    /// <summary>
    /// Whether its condition held when last checked (vanilla's <c>conditionMet</c>): always for an unconditional
    /// block, and for a conditional one whether the command block behind it last succeeded.
    /// </summary>
    public bool ConditionMet { get; set; }

    internal override bool SavesOnAnyThread => true;

    /// <summary>The data an operator's editor gets, which is what's saved (vanilla's <c>saveCustomOnly</c>).</summary>
    public NbtCompound EditorData()
    {
        var tag = new NbtCompound();
        this.Save(tag);
        return tag;
    }

    protected internal override void Load(NbtCompound tag)
    {
        this.Command.Load(tag, withName: true);
        this.Powered = tag.TryGetBool("powered", out var powered) && powered;
        this.ConditionMet = tag.TryGetBool("conditionMet", out var met) && met;
        this.Automatic = tag.TryGetBool("auto", out var automatic) && automatic;
    }

    protected internal override void Save(NbtCompound tag)
    {
        this.Command.Save(tag, withName: true);
        tag.Add(new NbtTag<bool>("powered", this.Powered));
        tag.Add(new NbtTag<bool>("conditionMet", this.ConditionMet));
        tag.Add(new NbtTag<bool>("auto", this.Automatic));
    }
}
