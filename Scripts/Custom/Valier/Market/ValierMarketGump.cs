// ValierMarketGump.cs
// Phase 1 Visual Pass
// - Keeps original Phase 1 market logic
// - Improves layout / styling / readability
// - Adds visual tags and a cleaner "market board" presentation
// - Still uses paging and the same buy/turn-in behavior

using System;
using System.Collections.Generic;

using Server;
using Server.Gumps;
using Server.Mobiles;
using Server.Network;
using Server.Targeting;

namespace Server.Custom.ValierMarket
{
    public class ValierMarketGump : Gump
    {
        private readonly PlayerMobile _pm;
        private readonly int _tab; // 0=orders, 1=scrolls
        private readonly int _pageIndex;

        private const int BtnTabOrders = 1;
        private const int BtnTabScrolls = 2;
        private const int BtnClose = 3;
        private const int BtnPrevPage = 4;
        private const int BtnNextPage = 5;

        private const int BtnOrderBackpackBase = 1000;
        private const int BtnOrderCrateBase = 2000;
        private const int BtnBuyScrollBase = 3000;

        private const int OrdersPerPage = 4;
        private const int ScrollsPerPage = 6;

        public ValierMarketGump(PlayerMobile pm, int tab) : this(pm, tab, 0)
        {
        }

        public ValierMarketGump(PlayerMobile pm, int tab, int pageIndex) : base(55, 35)
        {
            _pm = pm;
            _tab = tab;
            _pageIndex = Math.Max(0, pageIndex);

            Closable = true;
            Disposable = true;
            Dragable = true;
            Resizable = false;

            AddPage(0);

            int w = 720;
            int h = 520;

            AddBackground(0, 0, w, h, 5054);
            AddAlphaRegion(12, 12, w - 24, h - 24);

            AddLabel(18, 16, 1153, "Valier Market Exchange");
            AddHtml(18, 38, w - 36, 20, "<BASEFONT COLOR=#CCCCCC>Buy orders, scroll listings, and turn-in services. Visit daily for rotating opportunities.</BASEFONT>", false, false);
            AddHtml(18, 60, w - 36, 20, "<BASEFONT COLOR=#FFD966>Featured market boards rotate with the daily market state.</BASEFONT>", false, false);

            AddButton(18, 90, 4005, 4007, BtnTabOrders, GumpButtonType.Reply, 0);
            AddLabel(48, 90, _tab == 0 ? 68 : 1152, "Buy Orders");

            AddButton(150, 90, 4005, 4007, BtnTabScrolls, GumpButtonType.Reply, 0);
            AddLabel(180, 90, _tab == 1 ? 68 : 1152, "Scroll Market");

            AddImageTiled(16, 118, w - 32, 2, 2624);

            int y = 130;

            if (_tab == 0)
                DrawBuyOrders(ref y, w, h);
            else
                DrawScrolls(ref y, w, h);

            AddButton(w - 210, h - 38, 4014, 4016, BtnPrevPage, GumpButtonType.Reply, 0);
            AddLabel(w - 186, h - 38, 1152, "Prev");

            AddButton(w - 150, h - 38, 4005, 4007, BtnNextPage, GumpButtonType.Reply, 0);
            AddLabel(w - 126, h - 38, 1152, "Next");

            AddButton(w - 90, h - 38, 4017, 4019, BtnClose, GumpButtonType.Reply, 0);
            AddLabel(w - 66, h - 38, 1152, "Close");
        }

        private void DrawBuyOrders(ref int y, int w, int h)
        {
            List<BuyOrderDef> orders = ValierMarketSystem.GetActiveBuyOrders();

            AddHtml(18, y, w - 36, 20, "<BASEFONT COLOR=#9FD7FF>Specialty: resource turn-ins, daily coin earnings, and broker-assisted crate delivery.</BASEFONT>", false, false);
            y += 24;

            if (orders.Count == 0)
            {
                AddLabel(18, y, 1153, "No buy orders are active.");
                return;
            }

            int maxPage = (orders.Count - 1) / OrdersPerPage;
            int page = Math.Min(_pageIndex, maxPage);

            int earned = ValierMarketSystem.GetCoinsEarnedToday(_pm);
            int bestIndex = GetBestValueOrderIndex(orders);

            AddLabel(18, y, 1152, "Turn in from Backpack, or target a Turn-In Crate.");
            y += 20;

            AddLabel(18, y, 240, String.Format("Today: {0}/{1} Valier coins earned from orders.", earned, ValierMarketSystem.DailyCoinCapPerAccount));
            y += 20;

            AddLabel(18, y, 1152, String.Format("Page {0} / {1}", page + 1, maxPage + 1));
            y += 26;

            int start = page * OrdersPerPage;
            int end = Math.Min(start + OrdersPerPage, orders.Count);

            for (int i = start; i < end; i++)
            {
                BuyOrderDef o = orders[i];

                int turnedIn = ValierMarketSystem.GetOrderTurnInsToday(_pm, o.Id);
                int remaining = Math.Max(0, o.MaxTurnInsPerDay - turnedIn);

                AddImageTiled(18, y, w - 36, 72, 2624);

                AddHtml(26, y + 6, 180, 20, GetOrderTagHtml(i == start, i == bestIndex), false, false);
                AddHtml(26, y + 28, 430, 20, String.Format("<BASEFONT COLOR=#FFFFFF>{0}</BASEFONT>", o.DisplayName), false, false);
                AddHtml(26, y + 48, 520, 20,
                    String.Format("<BASEFONT COLOR=#CFCFCF>Need {0}  •  Reward {1}c + {2}g  •  Remaining today: {3}</BASEFONT>",
                    o.AmountPerTurnIn, o.CoinsReward, o.GoldReward, remaining),
                    false, false);

                AddButton(w - 228, y + 18, 4011, 4013, BtnOrderBackpackBase + i, GumpButtonType.Reply, 0);
                AddLabel(w - 202, y + 18, 1152, "Backpack");

                AddButton(w - 228, y + 42, 4011, 4013, BtnOrderCrateBase + i, GumpButtonType.Reply, 0);
                AddLabel(w - 202, y + 42, 1152, "Turn-In Crate");

                y += 82;
            }

            AddHtml(18, h - 88, w - 36, 40,
                "<BASEFONT COLOR=#B8D8B8>Tip: Drop resources into a Turn-In Crate, then target that crate from the broker for quick delivery.</BASEFONT>",
                false, false);
        }

        private void DrawScrolls(ref int y, int w, int h)
        {
            List<ScrollListingDef> list = ValierMarketSystem.GetActiveScrolls();

            AddHtml(18, y, w - 36, 20, "<BASEFONT COLOR=#D6B3FF>Specialty: limited scroll listings, daily purchase limits, and mixed gold/coin pricing.</BASEFONT>", false, false);
            y += 24;

            if (list.Count == 0)
            {
                AddLabel(18, y, 1153, "No scrolls are currently listed.");
                return;
            }

            int maxPage = (list.Count - 1) / ScrollsPerPage;
            int page = Math.Min(_pageIndex, maxPage);

            AddLabel(18, y, 1152, "Limited purchases per day (per account). Prices can be gold and/or Valier coins.");
            y += 20;

            AddLabel(18, y, 1152, String.Format("Page {0} / {1}", page + 1, maxPage + 1));
            y += 26;

            int start = page * ScrollsPerPage;
            int end = Math.Min(start + ScrollsPerPage, list.Count);

            for (int i = start; i < end; i++)
            {
                ScrollListingDef s = list[i];

                int bought = ValierMarketSystem.GetScrollBuysToday(_pm, s.Id);
                int remaining = Math.Max(0, s.LimitPerDay - bought);

                string price = "";
                if (s.PriceGold > 0)
                    price += s.PriceGold + "g ";

                if (s.PriceCoins > 0)
                    price += s.PriceCoins + "c";

                price = price.Trim();
                if (price.Length == 0)
                    price = "FREE";

                AddImageTiled(18, y, w - 36, 60, 2624);

                AddHtml(26, y + 6, 220, 20, GetScrollTagHtml(i == start, s), false, false);
                AddHtml(26, y + 26, 430, 20,
                    String.Format("<BASEFONT COLOR=#FFFFFF>{0}</BASEFONT>", s.DisplayName),
                    false, false);

                AddHtml(26, y + 44, 520, 20,
                    String.Format("<BASEFONT COLOR=#CFCFCF>{0} {1:0.0}  •  Price: {2}  •  Remaining today: {3}</BASEFONT>",
                    s.SkillNameText, s.Value, price, remaining),
                    false, false);

                AddButton(w - 130, y + 20, 4011, 4013, BtnBuyScrollBase + i, GumpButtonType.Reply, 0);
                AddLabel(w - 104, y + 20, 1152, "Buy");

                y += 70;
            }
        }

        private static int GetBestValueOrderIndex(List<BuyOrderDef> orders)
        {
            int best = -1;
            double bestScore = -1.0;

            for (int i = 0; i < orders.Count; i++)
            {
                BuyOrderDef o = orders[i];

                double score = 0.0;

                if (o.AmountPerTurnIn > 0)
                    score = ((o.CoinsReward * 1000.0) + o.GoldReward) / o.AmountPerTurnIn;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        private static string GetOrderTagHtml(bool featured, bool bestValue)
        {
            string html = "<BASEFONT COLOR=#80DFFF>[Turn-In]</BASEFONT> <BASEFONT COLOR=#FFD966>[Daily]</BASEFONT>";

            if (featured)
                html += " <BASEFONT COLOR=#B8FFB8>[Featured]</BASEFONT>";

            if (bestValue)
                html += " <BASEFONT COLOR=#FFA64D>[Best Value]</BASEFONT>";

            return html;
        }

        private static string GetScrollTagHtml(bool featured, ScrollListingDef s)
        {
            string html = "<BASEFONT COLOR=#FFD966>[Limited]</BASEFONT>";

            if (featured)
                html += " <BASEFONT COLOR=#B8FFB8>[Featured]</BASEFONT>";

            string n = (s != null ? (s.DisplayName ?? "") : "");

            if (n.IndexOf("recall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("gate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("resurrection", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                html += " <BASEFONT COLOR=#80DFFF>[Utility]</BASEFONT>";
            }

            return html;
        }

        public override void OnResponse(NetState sender, RelayInfo info)
        {
            if (_pm == null || _pm.Deleted)
                return;

            int id = info.ButtonID;

            if (id == BtnClose)
                return;

            if (id == BtnTabOrders)
            {
                _pm.SendGump(new ValierMarketGump(_pm, 0, 0));
                return;
            }

            if (id == BtnTabScrolls)
            {
                _pm.SendGump(new ValierMarketGump(_pm, 1, 0));
                return;
            }

            if (id == BtnPrevPage)
            {
                int nextPage = _pageIndex > 0 ? _pageIndex - 1 : 0;
                _pm.SendGump(new ValierMarketGump(_pm, _tab, nextPage));
                return;
            }

            if (id == BtnNextPage)
            {
                _pm.SendGump(new ValierMarketGump(_pm, _tab, _pageIndex + 1));
                return;
            }

            if (id >= BtnOrderBackpackBase && id < BtnOrderBackpackBase + 1000)
            {
                int idx = id - BtnOrderBackpackBase;
                List<BuyOrderDef> orders = ValierMarketSystem.GetActiveBuyOrders();

                if (idx >= 0 && idx < orders.Count)
                {
                    if (!ValierMarketSystem.IsNearBroker(_pm))
                        _pm.SendMessage(0x22, "You must be near the broker.");
                    else if (_pm.Backpack == null)
                        _pm.SendMessage(0x22, "You have no backpack.");
                    else
                    {
                        TurnInResult r = ValierMarketSystem.TryTurnIn(_pm, orders[idx], _pm.Backpack);
                        _pm.SendMessage(r.Success ? 0x59 : 0x22, r.Message);
                    }
                }

                _pm.SendGump(new ValierMarketGump(_pm, 0, _pageIndex));
                return;
            }

            if (id >= BtnOrderCrateBase && id < BtnOrderCrateBase + 1000)
            {
                int idx = id - BtnOrderCrateBase;
                List<BuyOrderDef> orders = ValierMarketSystem.GetActiveBuyOrders();

                if (idx >= 0 && idx < orders.Count)
                {
                    if (!ValierMarketSystem.IsNearBroker(_pm))
                    {
                        _pm.SendMessage(0x22, "You must be near the broker.");
                        _pm.SendGump(new ValierMarketGump(_pm, 0, _pageIndex));
                    }
                    else
                    {
                        _pm.SendMessage(0x59, "Target your Turn-In Crate.");
                        _pm.Target = new TurnInCrateTarget(orders[idx], _pageIndex);
                    }
                }

                return;
            }

            if (id >= BtnBuyScrollBase && id < BtnBuyScrollBase + 1000)
            {
                int idx = id - BtnBuyScrollBase;
                List<ScrollListingDef> list = ValierMarketSystem.GetActiveScrolls();

                if (idx >= 0 && idx < list.Count)
                {
                    string msg = ValierMarketSystem.TryBuyScroll(_pm, list[idx]);
                    _pm.SendMessage(0x59, msg);
                }

                _pm.SendGump(new ValierMarketGump(_pm, 1, _pageIndex));
                return;
            }
        }

        private class TurnInCrateTarget : Target
        {
            private readonly BuyOrderDef _order;
            private readonly int _pageIndex;

            public TurnInCrateTarget(BuyOrderDef order, int pageIndex) : base(12, false, TargetFlags.None)
            {
                _order = order;
                _pageIndex = pageIndex;
            }

            protected override void OnTarget(Mobile from, object targeted)
            {
                PlayerMobile pm = from as PlayerMobile;
                if (pm == null)
                    return;

                ValierTurnInCrate crate = targeted as ValierTurnInCrate;

                if (crate == null)
                {
                    pm.SendMessage(0x22, "That is not a Turn-In Crate.");
                    pm.SendGump(new ValierMarketGump(pm, 0, _pageIndex));
                    return;
                }

                TurnInResult r = ValierMarketSystem.TryTurnIn(pm, _order, crate);
                pm.SendMessage(r.Success ? 0x59 : 0x22, r.Message);

                pm.SendGump(new ValierMarketGump(pm, 0, _pageIndex));
            }
        }
    }
}
