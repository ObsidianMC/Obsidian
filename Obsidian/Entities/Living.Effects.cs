using Obsidian.API.Effects;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class Living
{
    private int effectTicks;

    private static bool IsLongerEffect(PotionEffectData incoming, PotionEffectData current) =>
        current.Duration != -1 && (incoming.Duration == -1 || incoming.Duration > current.Duration);

    private static PotionEffectData MergeEffect(PotionEffectData current, PotionEffectData incoming)
    {
        var changed = false;
        var result = current;
        if (incoming.Amplifier > current.Amplifier)
        {
            result = current with { Amplifier = incoming.Amplifier, Duration = incoming.Duration,
                HiddenEffect = IsLongerEffect(current, incoming) ? current : current.HiddenEffect };
            changed = true;
        }
        else if (IsLongerEffect(incoming, current))
        {
            if (incoming.Amplifier == current.Amplifier)
            {
                result = current with { Duration = incoming.Duration };
                changed = true;
            }
            else
            {
                result = current with { HiddenEffect = current.HiddenEffect == null ? incoming : MergeEffect(current.HiddenEffect, incoming) };
            }
        }
        return result with
        {
            Ambient = changed || !incoming.Ambient && current.Ambient ? incoming.Ambient : current.Ambient,
            ShowIcon = incoming.ShowIcon,
            ShowParticles = incoming.ShowParticles
        };
    }

    private static PotionEffectData TickEffectDuration(PotionEffectData effect) => effect with
    {
        Duration = effect.Duration == -1 ? -1 : Math.Max(0, effect.Duration - 1),
        HiddenEffect = effect.HiddenEffect is { } hidden ? TickEffectDuration(hidden) : null
    };

    private void SendPotionEffect(PotionEffectData effect)
    {
        PacketBroadcaster.QueuePacketToLevel(Level, new UpdateMobEffectPacket(EntityId, effect.Id, effect.Duration)
        { Amplifier = effect.Amplifier, Flags = PotionEffectFlags(effect) });
    }

    internal static EntityEffectFlags PotionEffectFlags(PotionEffectData effect) =>
        (effect.Ambient ? EntityEffectFlags.IsAmbient : EntityEffectFlags.None) |
        (effect.ShowParticles ? EntityEffectFlags.ShowParticles : EntityEffectFlags.None) |
        (effect.ShowIcon ? EntityEffectFlags.ShowIcon : EntityEffectFlags.None);

    internal static void WritePotionEffect(INbtWriter writer, PotionEffectData effect)
    {
        writer.WriteInt("id", effect.Id);
        writer.WriteInt("duration", effect.Duration);
        writer.WriteInt("amplifier", effect.Amplifier);
        writer.WriteBool("ambient", effect.Ambient);
        writer.WriteBool("show_particles", effect.ShowParticles);
        writer.WriteBool("show_icon", effect.ShowIcon);
        if (effect.HiddenEffect is { } hidden)
        {
            writer.WriteCompoundStart("hidden_effect");
            WritePotionEffect(writer, hidden);
            writer.EndCompound();
        }
    }

    private static PotionEffectData ReadPotionEffect(NbtCompound tag, int depth = 0) => new()
    {
        Id = tag.GetInt("id"),
        Duration = Math.Max(-1, tag.GetInt("duration")),
        Amplifier = Math.Clamp(tag.GetInt("amplifier"), 0, 255),
        Ambient = tag.TryGetBool("ambient", out var ambient) && ambient,
        ShowParticles = tag.TryGetBool("show_particles", out var particles) && particles,
        ShowIcon = tag.TryGetBool("show_icon", out var icon) && icon,
        HiddenEffect = depth < 255 && tag.TryGetTag<NbtCompound>("hidden_effect", out var hidden) ? ReadPotionEffect(hidden, depth + 1) : null
    };

    internal void RestorePotionEffect(NbtCompound tag)
    {
        var effect = ReadPotionEffect(tag);
        if (effect.Duration == -1 || effect.Duration > 0)
            activePotionEffects[effect.Id] = new() { EffectData = effect, CurrentDuration = effect.Duration };
    }
}
