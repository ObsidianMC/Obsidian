namespace Obsidian.Entities;

public partial class Player
{
    private int damageCooldown;
    private float lastIncomingDamage;

    public override async ValueTask DamageAsync(IEntity source, float amount = 1)
    {
        if (!float.IsFinite(amount) || amount <= 0 || !Alive || Respawning || source.Level != Level ||
            GameMode is GameMode.Creative or GameMode.Spectator) return;
        var incoming = amount;
        if (damageCooldown > 0)
        {
            if (incoming <= lastIncomingDamage) return;
            amount -= lastIncomingDamage;
        }
        else
        {
            damageCooldown = 10;
            HurtTime = 10;
        }
        lastIncomingDamage = incoming;
        await base.DamageAsync(source, amount);
    }
}
