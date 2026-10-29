using Obsidian.Entities;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// The client's attack swing, sent with each attack, block hit or swing at the air. It has no fields.
/// </summary>
public partial class PunchPacket
{
    public override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (player is not PlayerBase user || user.Level is not AbstractLevel level)
            return ValueTask.CompletedTask;

        // Like vanilla's handlePunch: the main arm swings for the players around, and the attack charges anew. On the
        // level tick, after the attack this swing went with, which reads the charge.
        level.EnqueueEntityAction(() =>
        {
            user.SwingForAttack(InteractionHand.MainHand);
            user.ResetAttackStrength();
            return ValueTask.CompletedTask;
        });

        return ValueTask.CompletedTask;
    }
}
