namespace Obsidian.Entities;

internal interface INetworkPlayer : IClientPlayer
{
    public ValueTask SynchronizeTrackedEntitiesAsync();
    public ValueTask TickNetworkLifecycleAsync();
    public void TrackEntity(IEntity entity);
    public void ForgetVisiblePlayer(IPlayer player);
}
