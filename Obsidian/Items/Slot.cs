using System.Collections.Generic;

namespace Obsidian.Items
{
    public class Slot
    {
        public bool Present { get; set; }

        public int Id { get; set; }

        public sbyte Count { get; set; }

        public ItemNbt ItemNbt { get; set; }
    }

    public class ItemNbt
    {
        public byte Slot { get; set; }

        /// <summary>
        /// this is the item durability
        /// </summary>
        public int Damage { get; set; }

        public int RepairCost { get; set; }
        public int CustomModelData { get; set; }

        public bool Unbreakable { get; set; }

        public List<string> CanDestroy { get; set; } = new List<string>();

        public List<Enchantment> Enchantments { get; private set; } = new List<Enchantment>();
        public List<Enchantment> StoredEnchantments { get; private set; } = new List<Enchantment>();

        public void AddEnchantment(Enchantment enchantment) => this.Enchantments.Add(enchantment);

        public void AddEnchantments(params Enchantment[] enchants) => this.Enchantments.AddRange(enchants);

        public void AddStoredEnchantment(Enchantment enchantment) => this.StoredEnchantments.Add(enchantment);
        public void AddStoredEnchantments(params Enchantment[] enchants) => this.StoredEnchantments.AddRange(enchants);
    }
}
