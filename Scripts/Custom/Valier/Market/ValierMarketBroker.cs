// ValierMarketBroker.cs
// Phase 1 Visual Pass
// - Keeps the original broker logic
// - Makes the broker stationary
// - Improves broker visual styling to better match later Valier market systems

using System;

using Server;
using Server.Mobiles;

namespace Server.Custom.ValierMarket
{
    [CorpseName("a broker's corpse")]
    public class ValierMarketBroker : BaseCreature
    {
        [Constructable]
        public ValierMarketBroker() : base(AIType.AI_Vendor, FightMode.None, 10, 1, 0.2, 0.4)
        {
            Female = Utility.RandomBool();
            Body = Female ? 0x191 : 0x190;
            Hue = Utility.RandomSkinHue();

            Name = Female ? NameList.RandomName("female") : NameList.RandomName("male");
            Title = "the market broker";

            Blessed = true;
            CantWalk = true;
            SpeechHue = 0x3B2;

            InitBrokerOutfit();
        }

        private void InitBrokerOutfit()
        {
            AddItem(new Server.Items.FancyShirt(Utility.RandomBlueHue()));
            AddItem(new Server.Items.LongPants(Utility.RandomNeutralHue()));
            AddItem(new Server.Items.BodySash(1153));
            AddItem(new Server.Items.Cloak(Utility.RandomBlueHue()));
            AddItem(new Server.Items.Boots(Utility.RandomNeutralHue()));
        }

        public ValierMarketBroker(Serial serial) : base(serial)
        {
        }

        public override bool ClickTitle
        {
            get { return true; }
        }

        public override void OnDoubleClick(Mobile from)
        {
            PlayerMobile pm = from as PlayerMobile;

            if (pm != null)
                ValierMarketSystem.OpenGump(pm, 0);
        }

        public override void OnSpeech(SpeechEventArgs e)
        {
            base.OnSpeech(e);

            if (e == null || e.Handled)
                return;

            PlayerMobile pm = e.Mobile as PlayerMobile;
            if (pm == null)
                return;

            string msg = (e.Speech ?? "").Trim();

            if (msg.Equals("market", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("orders", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("scrolls", StringComparison.OrdinalIgnoreCase) ||
                msg.Equals("broker", StringComparison.OrdinalIgnoreCase))
            {
                ValierMarketSystem.OpenGump(pm, 0);
                e.Handled = true;
            }
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write((int)2);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            int version = reader.ReadInt();

            if (Body == 0)
                Body = Female ? 0x191 : 0x190;

            Blessed = true;
            CantWalk = true;
            SpeechHue = 0x3B2;
        }
    }
}
