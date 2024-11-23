namespace Obsidian.API;
public interface INetworkSerializable<TValue>
{
    public static abstract void Write(TValue value, INetStreamWriter writer);
}
