using Microsoft.Extensions.Logging;
using Obsidian.API.Data;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Payloads;
using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class Player
{
    private readonly System.Threading.Lock attachmentSyncGate = new();
    private readonly Dictionary<AttachmentTarget, (AttachmentStorage Storage, long Revision, int Types)>
        attachmentsSent = [];

    // A player can disappear and spawn again between sync ticks, without changing its holder or revision.
    private void ResetVisiblePlayerAttachments(int entityId)
    {
        lock (this.attachmentSyncGate)
            this.attachmentsSent.Remove(new AttachmentTarget(AttachmentTargetKind.Entity, entityId));
    }

    private void ResetAttachmentSync()
    {
        lock (this.attachmentSyncGate)
            this.attachmentsSent.Clear();
    }

    void IPlayer.SynchronizeOwnState()
    {
        this.SynchronizeAttachments();
        this.SynchronizeMovementAbilities();
    }

    /// <summary>On the level tick after tracking. Never sends plugin attachment payloads to vanilla clients.</summary>
    internal void SynchronizeAttachments()
    {
        lock (this.attachmentSyncGate)
            this.SynchronizeAttachmentsUnderLock();
    }

    private void SynchronizeAttachmentsUnderLock()
    {
        if (this.Respawning || this.Level is not AbstractLevel level)
            return;

        if (this.Client.State != ClientState.Play || !this.Client.HasChannel(AttachmentPayload.Channel))
            return;

        var visible = new HashSet<AttachmentTarget>();
        this.SendAttachments(new AttachmentTarget(AttachmentTargetKind.Player), this, visible);
        foreach (var entity in level.Regions.Values.SelectMany(region => region.Entities.Values))
        {
            if (this.TrackedEntities.TryGetValue(entity.EntityId, out var uuid) && uuid == entity.Uuid)
            {
                this.SendAttachments(
                    new AttachmentTarget(AttachmentTargetKind.Entity, entity.EntityId), entity, visible);
            }
        }

        foreach (var player in this.visiblePlayers)
        {
            if (player.Alive && player is not Player { Respawning: true } && ReferenceEquals(player.Level, level))
            {
                this.SendAttachments(
                    new AttachmentTarget(AttachmentTargetKind.Entity, player.EntityId), player, visible);
            }
        }

        foreach (var packed in this.LoadedChunks)
        {
            NumericsHelper.LongToInts(packed, out var x, out var z);
            if (level.GetLoadedChunk(x, z) is IChunk chunk)
                this.SendAttachments(new AttachmentTarget(AttachmentTargetKind.Chunk, x, z), chunk, visible);
        }

        foreach (var target in this.attachmentsSent.Keys.Where(target => !visible.Contains(target)).ToArray())
            this.attachmentsSent.Remove(target);
    }

    private void SendAttachments(AttachmentTarget target, IAttachmentHolder holder, HashSet<AttachmentTarget> visible)
    {
        visible.Add(target);
        var storage = AttachmentStorage.For(holder);
        var revision = storage.Revision;
        var types = PluginDataBindings.Revision;
        if (this.attachmentsSent.TryGetValue(target, out var sent)
            && ReferenceEquals(sent.Storage, storage) && sent.Revision == revision && sent.Types == types)
        {
            return;
        }

        try
        {
            var data = storage.Snapshot();
            if (data.Count > 0 || this.attachmentsSent.ContainsKey(target))
            {
                var bytes = AttachmentPayload.Encode(target, data);
                var packet = CustomPayloadPacket.ClientboundPlay with
                {
                    Channel = AttachmentPayload.Channel,
                    PluginData = bytes
                };
                if (!this.Client.SendPacket(packet))
                {
                    return;
                }
            }

            this.attachmentsSent[target] = (storage, revision, types);
        }
        catch (Exception exception)
        {
            this.Logger.LogError(exception, "Failed to sync attachments for {Target}", target);
        }
    }
}
