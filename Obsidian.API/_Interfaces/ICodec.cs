using Obsidian.Nbt.Interfaces;

namespace Obsidian.API;

public interface ICodec
{
    public string Name { get; }

    public int Id { get; }

    public void WriteElement(INbtWriter writer);
}

