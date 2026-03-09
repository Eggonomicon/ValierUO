// ValierWeightReductionBags.cs (FIXED)
// Fixes weight reduction math for:
//   ValierIngotSatchel
//   ValierOreSatchel
//   ValierLumberSatchel
//   ValierTradesmanBackpack
//
// Root cause fixed:
// Old code used:
//   total -= total * (int)((double)WeightReduction / 100.0);
// which truncates 0.75 -> 0, so 75% reduction became 0%.
//
// This version uses proper percentage math and forces total recalculation.

using System;

using Server;
using Server.Items;

namespace Server.Items
{
    // ---------------------------------------------------------
    // Local fixed base so we don't have to modify ServUO core's
    // BaseResourceSatchel (which has the same percentage math bug).
    // ---------------------------------------------------------
    public abstract class ValierBaseResourceSatchel : BaseResourceSatchel
    {
        [CommandProperty(AccessLevel.GameMaster)]
        public new int WeightReduction
        {
            get { return base.WeightReduction; }
            set
            {
                int v = Math.Max(0, Math.Min(95, value));
                base.WeightReduction = v;
                InvalidateWeight();
            }
        }

        
        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(0); // version
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadInt(); // version
        }

public ValierBaseResourceSatchel(int id) : base(id)
        {
        }

        public ValierBaseResourceSatchel(Serial serial) : base(serial)
        {
        }

        public override int GetTotal(TotalType type)
        {
            int total = base.GetTotal(type);

            if (type == TotalType.Weight && total > 0 && base.WeightReduction > 0)
            {
                int reduction = (int)Math.Ceiling(total * (base.WeightReduction / 100.0));

                if (reduction < 0)
                    reduction = 0;
                else if (reduction > total)
                    reduction = total;

                total -= reduction;
            }

            return total;
        }
    }

    // -----------------------------
    // Resource satchels (restricted)
    // -----------------------------
    [Flipable(0xA272, 0xA273)]
    public class ValierIngotSatchel : ValierBaseResourceSatchel
    {
        public override Type[] HoldTypes { get { return new Type[] { typeof(BaseIngot) }; } }

        [Constructable]
        public ValierIngotSatchel() : base(0xA272)
        {
            Name = "Ingot Satchel";
            WeightReduction = 75;
        }

        public ValierIngotSatchel(Serial serial) : base(serial) { }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(1); // version
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            int version = reader.ReadInt();

            // Ensure old saves get the intended reduction
            if (WeightReduction <= 0)
                WeightReduction = 75;

            InvalidateWeight();
        }
    }

    [Flipable(0xA272, 0xA273)]
    public class ValierOreSatchel : ValierBaseResourceSatchel
    {
        public override Type[] HoldTypes { get { return new Type[] { typeof(BaseOre), typeof(Granite), typeof(Saltpeter) }; } }

        [Constructable]
        public ValierOreSatchel() : base(0xA272)
        {
            Name = "Ore Satchel";
            WeightReduction = 75;
        }

        public ValierOreSatchel(Serial serial) : base(serial) { }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(1); // version
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            int version = reader.ReadInt();

            if (WeightReduction <= 0)
                WeightReduction = 75;

            InvalidateWeight();
        }
    }

    [Flipable(0xA274, 0xA275)]
    public class ValierLumberSatchel : ValierBaseResourceSatchel
    {
        public override Type[] HoldTypes { get { return new Type[] { typeof(BaseLog), typeof(Board) }; } }

        [Constructable]
        public ValierLumberSatchel() : base(0xA274)
        {
            Name = "Lumber Satchel";
            WeightReduction = 75;
        }

        public ValierLumberSatchel(Serial serial) : base(serial) { }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(1); // version
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            int version = reader.ReadInt();

            if (WeightReduction <= 0)
                WeightReduction = 75;

            InvalidateWeight();
        }
    }

    // -----------------------------
    // Tradesman's backpack (generic)
    // -----------------------------
    public class ValierTradesmanBackpack : Backpack
    {
        private int _weightReduction = 65;

        [CommandProperty(AccessLevel.GameMaster)]
        public int WeightReduction
        {
            get { return _weightReduction; }
            set
            {
                _weightReduction = Math.Max(0, Math.Min(95, value));
                InvalidateProperties();
                InvalidateWeight();
            }
        }

        [Constructable]
        public ValierTradesmanBackpack()
        {
            Name = "Tradesman's Backpack";
            LootType = LootType.Blessed;
        }

        public ValierTradesmanBackpack(Serial serial) : base(serial) { }

        public override void GetProperties(ObjectPropertyList list)
        {
            base.GetProperties(list);

            if (_weightReduction != 0)
                list.Add(1072210, _weightReduction.ToString()); // Weight reduction: ~1_PERCENTAGE~%
        }

        public override int GetTotal(TotalType type)
        {
            int total = base.GetTotal(type);

            if (type == TotalType.Weight && total > 0 && _weightReduction > 0)
            {
                int reduction = (int)Math.Ceiling(total * (_weightReduction / 100.0));

                if (reduction < 0)
                    reduction = 0;
                else if (reduction > total)
                    reduction = total;

                total -= reduction;
            }

            return total;
        }

        public void InvalidateWeight()
        {
            if (RootParent is Mobile)
            {
                ((Mobile)RootParent).UpdateTotals();
            }
        }

        public override void AddItem(Item dropped)
        {
            base.AddItem(dropped);
            InvalidateWeight();
        }

        public override void RemoveItem(Item dropped)
        {
            base.RemoveItem(dropped);
            InvalidateWeight();
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(1); // version
            writer.Write(_weightReduction);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            int version = reader.ReadInt();

            if (version >= 0)
                _weightReduction = reader.ReadInt();

            _weightReduction = Math.Max(0, Math.Min(95, _weightReduction));

            if (_weightReduction <= 0)
                _weightReduction = 65;
        }
    }
}
