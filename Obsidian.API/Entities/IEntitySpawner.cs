using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.API.Entities;
public interface IEntitySpawner
{
    IEntitySpawner WithEntityType(EntityType type);
    IEntitySpawner AsBaby();
    IEntitySpawner AtPosition(VectorF position);
    IEntitySpawner WithEntityId(int entityId);
    IEntitySpawner WithCustomName(string name, bool visible = true);
    IEntitySpawner WithLivingBitMask(LivingBitMask bitMask);
    IEntitySpawner WithAmbientPotionEffect(bool ambient);
    IEntitySpawner WithAbsorbedArrows(int arrows);
    IEntitySpawner WithAbsorbtionAmount(int amount);
    IEntitySpawner WithAbsorbedStingers(int stingers);
    IEntitySpawner IsBurning();
    IEntitySpawner IsGlowing();
    IEntity Spawn();
}
