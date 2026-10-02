namespace Obsidian.Entities.AI;

internal static class RandomPosition
{
    public static VectorF? Find(PathfinderMob mob, int horizontalRange, VectorF? awayFrom = null)
    {
        var evaluator = new WalkNodeEvaluator(mob);
        VectorF? best = null;
        var bestScore = float.NegativeInfinity;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var x = (int)MathF.Floor(mob.Position.X) + mob.Random.Next(-horizontalRange, horizontalRange + 1);
            var z = (int)MathF.Floor(mob.Position.Z) + mob.Random.Next(-horizontalRange, horizontalRange + 1);
            var position = evaluator.FindGround(x, z, mob.Position.Y + mob.Random.Next(-3, 4));
            if (position is not VectorF candidate)
                continue;

            var score = awayFrom is VectorF threat ? (candidate - threat).MagnitudeSquared() : mob.Random.NextSingle();
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }
}

public sealed class FloatGoal(PathfinderMob mob) : Goal
{
    public override GoalFlags Flags => GoalFlags.Jump;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => mob.InWater || mob.InLava;
    public override ValueTask TickAsync()
    {
        if (mob.Random.NextSingle() < 0.8f)
            mob.JumpControl.Jump();
        return default;
    }
}

public abstract class NavigationGoal(PathfinderMob mob, float speed) : Goal
{
    protected PathfinderMob Mob { get; } = mob;
    protected Navigator Navigation => (Navigator)Mob.Navigator!;
    protected float Speed { get; } = speed;
    public override GoalFlags Flags => GoalFlags.Move;
    public override void Stop() => Navigation.Stop();

    protected void MoveTo(VectorF position)
    {
        Navigation.SpeedModifier = Speed;
        Navigation.NavigateTo(position);
    }

    protected void MoveTo(IEntity entity)
    {
        Navigation.SpeedModifier = Speed;
        Navigation.NavigateTo(entity);
    }
}

public sealed class PanicGoal(PathfinderMob mob, float speed) : NavigationGoal(mob, speed)
{
    private VectorF destination;
    public override bool CanUse()
    {
        if (!Mob.Burning && (Mob.LastAttacker == null || Mob.AiTick - Mob.LastHurtTick > 100))
            return false;

        VectorF? position = null;
        if (Mob.Burning)
        {
            var origin = (Vector)Mob.Position.Floor();
            for (var x = origin.X - 5; x <= origin.X + 5; x++)
            for (var y = origin.Y - 1; y <= origin.Y + 1; y++)
            for (var z = origin.Z - 5; z <= origin.Z + 5; z++)
            {
                var waterPoint = new VectorF(x + 0.5f, y, z + 0.5f);
                if (Mob.Terrain.GetBlock(new Vector(x, y, z))?.Material == Material.Water &&
                    (position == null || (waterPoint - Mob.Position).MagnitudeSquared() < (position.Value - Mob.Position).MagnitudeSquared()))
                    position = waterPoint;
            }
        }
        position ??= RandomPosition.Find(Mob, 5);
        if (position is not VectorF point)
            return false;
        destination = point;
        return true;
    }
    public override void Start() => MoveTo(destination);
    public override bool CanContinue() => Navigation.IsNavigating;
}

public sealed class RandomStrollGoal(PathfinderMob mob, float speed) : NavigationGoal(mob, speed)
{
    private VectorF destination;
    public override bool CanUse()
    {
        if (Mob.AttackTarget != null || Mob.Random.Next(60) != 0)
            return false;
        var position = RandomPosition.Find(Mob, 10);
        if (position is not VectorF point)
            return false;
        destination = point;
        return true;
    }
    public override void Start() => MoveTo(destination);
    public override bool CanContinue() => Navigation.IsNavigating;
}

public sealed class LookAtPlayerGoal(Mob mob, float range) : Goal
{
    private IPlayer? player;
    private long endTick;
    public override GoalFlags Flags => GoalFlags.Look;
    public override bool CanUse()
    {
        if (mob.Random.NextSingle() >= 0.02f)
            return false;
        player = mob.Level.GetPlayersInRange(mob.Position, range)
            .Where(target => target.Health > 0 && target.Gamemode != Gamemode.Spectator)
            .OrderBy(target => (target.Position - mob.Position).MagnitudeSquared()).FirstOrDefault();
        return player != null;
    }
    public override void Start() => endTick = mob.AiTick + 40 + mob.Random.Next(40);
    public override bool CanContinue() => player != null && player.Level == mob.Level && player.Health > 0 &&
        player.Gamemode != Gamemode.Spectator && mob.AiTick < endTick &&
        (player.Position - mob.Position).MagnitudeSquared() <= range * range;
    public override ValueTask TickAsync()
    {
        mob.LookControl.LookAt(player!.Position + new VectorF(0, (float)(player.HeadY - player.Position.Y), 0));
        return default;
    }
    public override void Stop() => player = null;
}

public sealed class RandomLookAroundGoal(Mob mob) : Goal
{
    private VectorF direction;
    private long endTick;
    public override GoalFlags Flags => GoalFlags.Look;
    public override bool CanUse() => mob.Random.NextSingle() < 0.02f;
    public override void Start()
    {
        var angle = mob.Random.NextSingle() * MathF.Tau;
        direction = new VectorF(MathF.Cos(angle), 0, MathF.Sin(angle));
        endTick = mob.AiTick + 20 + mob.Random.Next(20);
    }
    public override bool CanContinue() => mob.AiTick < endTick;
    public override ValueTask TickAsync()
    {
        mob.LookControl.LookAt(mob.EyePosition + direction);
        return default;
    }
}

public sealed class TemptGoal(Animal pig, Func<Obsidian.API.Inventory.ItemStack?, bool> food) : NavigationGoal(pig, 1.2f)
{
    private IPlayer? player;
    private long cooldownEnd;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool CanUse()
    {
        if (pig.AiTick < cooldownEnd)
            return false;
        player = pig.Level.GetPlayersInRange(pig.Position, 10)
            .Where(IsTempting).OrderBy(target => (target.Position - pig.Position).MagnitudeSquared()).FirstOrDefault();
        return player != null;
    }
    private bool IsTempting(IPlayer target) => target.Health > 0 && target.Gamemode != Gamemode.Spectator &&
        (food(target.GetHeldItem()) || food(target.GetOffHandItem()));
    public override bool CanContinue() => player != null && player.Level == pig.Level && IsTempting(player) &&
        (player.Position - pig.Position).MagnitudeSquared() <= 100;
    public override ValueTask TickAsync()
    {
        pig.LookControl.LookAt(player!);
        if ((player!.Position - pig.Position).MagnitudeSquared() < 6.25f)
            Navigation.Stop();
        else
            MoveTo(player);
        return default;
    }
    public override void Stop()
    {
        base.Stop();
        player = null;
        cooldownEnd = pig.AiTick + 100;
    }
}

public sealed class FollowParentGoal(Animal pig) : NavigationGoal(pig, 1.1f)
{
    private Animal? parent;
    private long nextPathTick;
    public override GoalFlags Flags => GoalFlags.None;
    public override bool CanUse()
    {
        if (!pig.IsBaby)
            return false;
        parent = pig.GetEntitiesNear(16).OfType<Animal>().Where(target => target.Type == pig.Type && !target.IsBaby && target.Alive && !target.IsRemoved &&
            MathF.Abs(target.Position.X - pig.Position.X) <= 8 + pig.Dimension.Width &&
            MathF.Abs(target.Position.Y - pig.Position.Y) <= 4 + pig.Dimension.Height &&
            MathF.Abs(target.Position.Z - pig.Position.Z) <= 8 + pig.Dimension.Width)
            .OrderBy(target => (target.Position - pig.Position).MagnitudeSquared()).FirstOrDefault();
        return parent != null && (parent.Position - pig.Position).MagnitudeSquared() >= 9;
    }
    public override bool CanContinue()
    {
        if (!pig.IsBaby || parent == null || !parent.Alive || parent.IsRemoved || parent.Level != pig.Level)
            return false;
        var distance = (parent.Position - pig.Position).MagnitudeSquared();
        return distance is >= 9 and <= 256;
    }
    public override ValueTask TickAsync()
    {
        if (pig.AiTick >= nextPathTick)
        {
            nextPathTick = pig.AiTick + 10;
            MoveTo(parent!);
        }
        return default;
    }
    public override void Stop()
    {
        parent = null;
    }
}

public sealed class BreedGoal(Animal pig) : NavigationGoal(pig, 1)
{
    private Animal? mate;
    private long startTick;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse()
    {
        if (!pig.CanBreed)
            return false;
        mate = pig.GetEntitiesNear(8).OfType<Animal>().Where(target => target.Type == pig.Type && target.CanBreed)
            .OrderBy(target => (target.Position - pig.Position).MagnitudeSquared()).FirstOrDefault();
        return mate != null;
    }
    public override void Start() => startTick = pig.AiTick;
    public override bool CanContinue() => pig.CanBreed && mate != null && mate.CanBreed && mate.Level == pig.Level && pig.AiTick - startTick < 60;
    public override ValueTask TickAsync()
    {
        pig.LookControl.LookAt(mate!);
        MoveTo(mate!);
        if (pig.AiTick - startTick >= 59 && (mate!.Position - pig.Position).MagnitudeSquared() < 9)
            pig.BreedWith(mate);
        return default;
    }
    public override void Stop()
    {
        base.Stop();
        mate = null;
    }
}

public sealed class NearestAttackableTargetGoal(PathfinderMob zombie, Func<IEntity, bool> predicate, bool mustSee = true) : Goal
{
    private IEntity? candidate;
    private int unseenTicks;
    public override GoalFlags Flags => GoalFlags.Target;
    public override bool CanUse()
    {
        if (zombie.Random.Next(5) != 0)
            return false;
        candidate = zombie.Level.GetEntitiesInRange(zombie.Position, zombie.FollowRange)
            .Where(target => predicate(target) && zombie.IsValidTarget(target) && (!mustSee || zombie.CanSee(target)))
            .OrderBy(target => (target.Position - zombie.Position).MagnitudeSquared()).FirstOrDefault();
        return candidate != null;
    }
    public override void Start()
    {
        zombie.AttackTarget = candidate;
        unseenTicks = 0;
    }
    public override bool CanContinue()
    {
        var target = zombie.AttackTarget;
        if (target == null || !zombie.IsValidTarget(target) ||
            (target.Position - zombie.Position).MagnitudeSquared() > zombie.FollowRange * zombie.FollowRange)
            return false;
        unseenTicks = !mustSee || zombie.CanSee(target) ? 0 : unseenTicks + 2;
        return unseenTicks <= 60;
    }
    public override void Stop()
    {
        zombie.AttackTarget = null;
        candidate = null;
    }
}

public sealed class HurtByTargetGoal(PathfinderMob zombie) : Goal
{
    private long handledHurtTick = -100;
    private IEntity? candidate;
    public override GoalFlags Flags => GoalFlags.Target;
    public override bool CanUse()
    {
        candidate = zombie.LastHurtTick != handledHurtTick ? zombie.LastAttacker : zombie.AlertedTarget;
        return candidate != null && zombie.IsValidTarget(candidate);
    }
    public override void Start()
    {
        handledHurtTick = zombie.LastHurtTick;
        zombie.AttackTarget = candidate;
        zombie.AlertedTarget = null;
        foreach (var ally in zombie.GetEntitiesNear(zombie.FollowRange).OfType<PathfinderMob>().Where(ally => ally.Type == zombie.Type))
        {
            if (ally.AttackTarget == null && ally.AlertedTarget == null && ally.IsValidTarget(candidate!))
                ally.AlertedTarget = candidate;
        }
    }
    public override bool CanContinue() => zombie.AttackTarget is IEntity target && zombie.IsValidTarget(target) &&
        (target.Position - zombie.Position).MagnitudeSquared() <= zombie.FollowRange * zombie.FollowRange;
    public override void Stop() => zombie.AttackTarget = null;
}

public class MeleeAttackGoal(PathfinderMob mob, float speed = 1) : NavigationGoal(mob, speed)
{
    private long lastUseTick = -20;
    private long nextAttackTick;
    private long nextPathTick;
    private VectorF lastTargetPosition;
    protected int AttackCooldown => (int)Math.Max(0, nextAttackTick - Mob.AiTick);
    public override bool RequiresUpdateEveryTick => true;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;

    public override bool CanUse()
    {
        if (Mob.AiTick - lastUseTick < 20)
            return false;
        lastUseTick = Mob.AiTick;
        return Mob.AttackTarget is IEntity target && Mob.IsValidTarget(target) &&
            (Mob.IsWithinMeleeRange(target) || new PathFinder(Mob).FindPath(target.Position) != null);
    }

    public override bool CanContinue() => Mob.AttackTarget is IEntity target && Mob.IsValidTarget(target) &&
        (Navigation.IsNavigating || Mob.IsWithinMeleeRange(target));

    public override void Start()
    {
        nextAttackTick = Mob.AiTick;
        nextPathTick = Mob.AiTick;
        MoveTo(Mob.AttackTarget!);
        Mob.SetAggressive(true);
    }

    public override async ValueTask TickAsync()
    {
        if (Mob.AttackTarget is not IEntity target || !Mob.IsValidTarget(target))
            return;
        Mob.LookControl.LookAt(target.Position + new VectorF(0, target is Mob other ? other.EyeHeight : target.Dimension.Height * 0.85f, 0), 30, 30);
        if (Mob.CanSee(target) && Mob.AiTick >= nextPathTick &&
            (lastTargetPosition == VectorF.Zero || (lastTargetPosition - target.Position).MagnitudeSquared() >= 1 || Mob.Random.NextSingle() < 0.05f))
        {
            lastTargetPosition = target.Position;
            var distance = (target.Position - Mob.Position).MagnitudeSquared();
            nextPathTick = Mob.AiTick + 4 + Mob.Random.Next(7) + (distance > 1024 ? 10 : distance > 256 ? 5 : 0);
            MoveTo(target);
        }
        if (AttackCooldown == 0 && Mob.IsWithinMeleeRange(target) && Mob.CanSee(target))
        {
            nextAttackTick = Mob.AiTick + 20;
            await Mob.PerformMeleeAttackAsync(target);
        }
    }

    public override void Stop()
    {
        base.Stop();
        Mob.SetAggressive(false);
    }
}

public sealed class ZombieAttackGoal(Zombie zombie) : MeleeAttackGoal(zombie)
{
    private int raiseArmTicks;
    public override void Start()
    {
        base.Start();
        raiseArmTicks = 0;
        zombie.SetAggressive(false);
    }

    public override async ValueTask TickAsync()
    {
        await base.TickAsync();
        zombie.SetAggressive(++raiseArmTicks >= 5 && AttackCooldown < 10);
    }
}
