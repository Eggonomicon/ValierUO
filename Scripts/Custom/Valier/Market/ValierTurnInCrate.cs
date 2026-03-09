// ValierTurnInCrate.cs
// Phase 1 Visual Pass
// - Improves the crate name and player-facing feedback
// - Keeps original storage/turn-in behavior intact

using System;

using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.ValierMarket
{
    public class ValierTurnInCrate : WoodenBox
    {
        [Constructable]
        public ValierTurnInCrate()
        {
            Name = "Valier Turn-In Crate";
            Hue = 0x59B;
            Movable = true;
            LootType = LootType.Blessed;
        }

        public ValierTurnInCrate(Serial serial) : base(serial)
        {
        }

        public override void OnDoubleClick(Mobile from)
        {
            base.OnDoubleClick(from);

            PlayerMobile pm = from as PlayerMobile;

            if (pm != null)
                pm.SendMessage(0x59, "Use the market broker and choose 'Turn in Crate', then target this crate.");
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write((int)1);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            int version = reader.ReadInt();

            if (String.IsNullOrEmpty(Name))
                Name = "Valier Turn-In Crate";
        }
    }
}
