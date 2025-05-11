using Obsidian.Nbt.Interfaces;

namespace Obsidian.API;
public interface INbtSerializable
{
    public void Write(INbtWriter writer);
}
