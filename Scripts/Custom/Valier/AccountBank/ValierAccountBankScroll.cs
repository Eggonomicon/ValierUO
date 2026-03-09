// ValierAccountBankScroll.cs
// One-time unlock item for the Account Bank system (per account)

using System;

using Server;
using Server.Accounting;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.Valier
{
    public class ValierAccountBankScroll : Item
    {
        [Constructable]
        public ValierAccountBankScroll() : base(0x14F0)
        {
            Name = "Valier Account Bank Scroll";
            Hue = 0x489;
            Weight = 0.1;
        }

        public ValierAccountBankScroll(Serial serial) : base(serial)
        {
        }

        public override void OnDoubleClick(Mobile from)
        {
            PlayerMobile pm = from as PlayerMobile;

            if (pm == null)
                return;

            if (!IsChildOf(pm.Backpack))
            {
                pm.SendMessage(0x22, "That must be in your backpack to use it.");
                return;
            }

            Account acc = pm.Account as Account;

            if (acc == null)
            {
                pm.SendMessage(0x22, "No account found.");
                return;
            }

            ValierAccountBankSystem.SetUnlocked(acc, true);

            pm.SendMessage(0x59, "Your account bank has been unlocked!");
            Delete();
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write(0);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadInt();
        }
    }
}
