// QuickSellBag.cs (Openable + Context Menu, C# 7.3 compatible)
// ValierUO / ServUO 57.x: "Sell-all" bag
//
// Behavior:
// - Double-click opens the bag normally.
// - Context menu entry (same style as SalvageBag): "Sell" (cliloc 6104)
// - Also provides command: [QuickSell  (uses first QuickSellBag in your backpack)
//
// Selling uses BaseVendor.OnSellItems(...) so it respects normal vendor rules.

using System;
using System.Collections.Generic;

using Server;
using Server.Commands;
using Server.ContextMenus;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Targeting;

namespace Server.Custom.Valier
{
    public class QuickSellBag : Bag
    {
        private static bool _commandsRegistered;

        private bool _includeSubContainers = true;
        private bool _skipContainers = true;
        private int _vendorRange = 3;
        private int _maxItemsPerUse = 400;

        [CommandProperty(AccessLevel.GameMaster)]
        public bool IncludeSubContainers
        {
            get { return _includeSubContainers; }
            set { _includeSubContainers = value; }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool SkipContainers
        {
            get { return _skipContainers; }
            set { _skipContainers = value; }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int VendorRange
        {
            get { return _vendorRange; }
            set { _vendorRange = Math.Max(1, Math.Min(12, value)); }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int MaxItemsPerUse
        {
            get { return _maxItemsPerUse; }
            set { _maxItemsPerUse = Math.Max(25, Math.Min(5000, value)); }
        }

        public static void Initialize()
        {
            if (_commandsRegistered)
                return;

            _commandsRegistered = true;

            CommandSystem.Register("QuickSell", AccessLevel.Player, OnQuickSellCommand);
        }

        private static void OnQuickSellCommand(CommandEventArgs e)
        {
            PlayerMobile pm = e.Mobile as PlayerMobile;

            if (pm == null)
                return;

            if (pm.Backpack == null)
            {
                pm.SendMessage(0x22, "You have no backpack.");
                return;
            }

            QuickSellBag bag = FindFirstQuickSellBag(pm.Backpack);

            if (bag == null)
            {
                pm.SendMessage(0x22, "You do not have a Quick Sell Bag in your backpack.");
                return;
            }

            bag.BeginSell(pm);
        }

        private static QuickSellBag FindFirstQuickSellBag(Container c)
        {
            if (c == null)
                return null;

            Item[] items = c.FindItemsByType(typeof(QuickSellBag), true);

            for (int i = 0; i < items.Length; i++)
            {
                QuickSellBag bag = items[i] as QuickSellBag;
                if (bag != null && !bag.Deleted)
                    return bag;
            }

            return null;
        }

        [Constructable]
        public QuickSellBag()
        {
            Name = "Quick Sell Bag";
            Hue = 0x489;
            LootType = LootType.Blessed;
            Weight = 1.0;
        }

        public QuickSellBag(Serial serial) : base(serial)
        {
        }

        // Open normally
        public override void OnDoubleClick(Mobile from)
        {
            base.OnDoubleClick(from);
        }

        // Context menu like SalvageBag
        public override void GetContextMenuEntries(Mobile from, List<ContextMenuEntry> list)
        {
            base.GetContextMenuEntries(from, list);

            if (from == null || list == null)
                return;

            if (!from.Alive)
                return;

            bool enabled = IsChildOf(from.Backpack) && HasPotentialSellables();

            list.Add(new SellBagEntry(this, enabled));
        }

        private bool HasPotentialSellables()
        {
            // Lightweight "enable" check to avoid a dead menu item.
            // Vendor will do the authoritative checks.
            try
            {
                Item[] items = FindItemsByType(typeof(Item), _includeSubContainers);

                for (int i = 0; i < items.Length; i++)
                {
                    Item it = items[i];
                    if (it == null || it.Deleted)
                        continue;

                    if (it == this)
                        continue;

                    if (_skipContainers && it is Container)
                        continue;

                    if (!it.Movable || it.LootType == LootType.Blessed || it.LootType == LootType.Newbied || it.LootType == LootType.Cursed)
                        continue;

                    // For containers, only if empty (to avoid weird surprises)
                    Container c = it as Container;
                    if (c != null && c.Items.Count != 0)
                        continue;

                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private class SellBagEntry : ContextMenuEntry
        {
            private readonly QuickSellBag _bag;

            public SellBagEntry(QuickSellBag bag, bool enabled)
                : base(6104) // "Sell"
            {
                _bag = bag;

                if (!enabled)
                    Flags |= CMEFlags.Disabled;
            }

            public override void OnClick()
            {
                if (_bag == null || _bag.Deleted)
                    return;

                Mobile from = Owner.From;

                if (from != null && from.CheckAlive())
                    _bag.BeginSell(from);
            }
        }

        public void BeginSell(Mobile from)
        {
            if (from == null || Deleted)
                return;

            if (!IsChildOf(from.Backpack))
            {
                from.SendMessage(0x22, "That must be in your backpack to sell from it.");
                return;
            }

            BaseVendor vendor = FindNearestVendor(from, _vendorRange);

            if (vendor != null)
            {
                SellAll(from, vendor);
                return;
            }

            from.SendMessage(0x59, "Target a vendor to sell the contents of this bag.");
            from.Target = new VendorTarget(this);
        }

        private static BaseVendor FindNearestVendor(Mobile from, int range)
        {
            if (from == null || from.Map == null)
                return null;

            BaseVendor best = null;
            int bestDist = int.MaxValue;

            foreach (Mobile mob in from.GetMobilesInRange(range))
            {
                BaseVendor v = mob as BaseVendor;
                if (v == null)
                    continue;

                if (v.Deleted || v.Map != from.Map)
                    continue;

                if (!from.InLOS(v) || !from.InRange(v, range))
                    continue;

                int d = (int)from.GetDistanceToSqrt(v);

                if (d < bestDist)
                {
                    best = v;
                    bestDist = d;
                }
            }

            return best;
        }

        private void SellAll(Mobile from, BaseVendor vendor)
        {
            if (from == null || vendor == null || Deleted)
                return;

            if (!from.InRange(vendor, _vendorRange) || !from.InLOS(vendor))
            {
                from.SendMessage(0x22, "You are too far away from the vendor.");
                return;
            }

            List<Item> items = new List<Item>();
            GetItemsRecursive(this, items, _includeSubContainers, _maxItemsPerUse);

            if (items.Count == 0)
            {
                from.SendMessage(0x59, "There is nothing in the bag to sell.");
                return;
            }

            List<SellItemResponse> sell = new List<SellItemResponse>();

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];

                if (item == null || item.Deleted)
                    continue;

                if (!item.IsChildOf(this))
                    continue;

                if (_skipContainers && item is Container)
                    continue;

                if (!item.Movable || item.LootType == LootType.Blessed || item.LootType == LootType.Newbied || item.LootType == LootType.Cursed)
                    continue;

                // Keep standard safety: don't sell weird/quest items
                if (!item.IsStandardLoot())
                    continue;

                Container cont = item as Container;
                if (cont != null && cont.Items.Count != 0)
                    continue;

                int amount = 1;
                if (item.Stackable && item.Amount > 1)
                    amount = item.Amount;

                sell.Add(new SellItemResponse(item, amount));
            }

            if (sell.Count == 0)
            {
                from.SendMessage(0x22, "The vendor would not buy anything from that bag.");
                return;
            }

            if (sell.Count > BaseVendor.MaxSell)
            {
                from.SendMessage(0x22, "You may only sell {0} items at a time. (Bag has {1} sellable items.)", BaseVendor.MaxSell, sell.Count);
                return;
            }

            bool ok = vendor.OnSellItems(from, sell);

            if (!ok)
            {
                from.SendMessage(0x22, "The vendor refused the sale.");
                return;
            }

            from.SendMessage(0x59, "Sold all sellable items from your Quick Sell Bag.");
        }

        private static void GetItemsRecursive(Container c, List<Item> list, bool recurse, int max)
        {
            if (c == null || list == null)
                return;

            Item[] items = c.Items.ToArray();

            for (int i = 0; i < items.Length; i++)
            {
                if (list.Count >= max)
                    return;

                Item it = items[i];

                if (it == null)
                    continue;

                Container sub = it as Container;

                if (sub != null && recurse)
                {
                    GetItemsRecursive(sub, list, recurse, max);
                }
                else
                {
                    list.Add(it);
                }
            }
        }

        private class VendorTarget : Target
        {
            private readonly QuickSellBag _bag;

            public VendorTarget(QuickSellBag bag) : base(12, false, TargetFlags.None)
            {
                _bag = bag;
            }

            protected override void OnTarget(Mobile from, object targeted)
            {
                if (_bag == null || _bag.Deleted)
                    return;

                BaseVendor v = targeted as BaseVendor;

                if (v != null)
                {
                    _bag.SellAll(from, v);
                }
                else
                {
                    from.SendMessage(0x22, "That is not a vendor.");
                }
            }
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write(3); // version
            writer.Write(_includeSubContainers);
            writer.Write(_skipContainers);
            writer.Write(_vendorRange);
            writer.Write(_maxItemsPerUse);
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);

            int version = reader.ReadInt();

            if (version >= 1)
            {
                _includeSubContainers = reader.ReadBool();
                _skipContainers = reader.ReadBool();
                _vendorRange = reader.ReadInt();
                _maxItemsPerUse = reader.ReadInt();
            }
            else
            {
                _includeSubContainers = true;
                _skipContainers = true;
                _vendorRange = 3;
                _maxItemsPerUse = 400;
            }
        }
    }
}