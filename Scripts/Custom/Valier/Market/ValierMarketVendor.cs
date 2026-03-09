using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

using Server;
using Server.Commands;
using Server.ContextMenus;
using Server.Items;
using Server.Mobiles;
using Server.Targeting;

namespace Server.Custom.Valier.Market
{
    public sealed class ValierVendorStockEntry
    {
        public string TypeName;
        public int Price;
        public int Amount;

        public ValierVendorStockEntry() { }
        public ValierVendorStockEntry(string typeName, int price, int amount)
        {
            TypeName = typeName;
            Price = price;
            Amount = amount;
        }
    }

    public sealed class ValierMarketEntryDef
    {
        public string TypeName;
        public int MinQty;
        public int MaxQty;
        public int MinPrice;
        public int MaxPrice;
        public int Weight;
    }

    public sealed class ValierMarketProfileDef
    {
        public string Id;
        public string VendorName;
        public string Title;
        public int MaxEntries;
        public List<ValierMarketEntryDef> Entries = new List<ValierMarketEntryDef>();
    }

    public static class ValierMarketSystem
    {
        public static readonly Dictionary<string, ValierMarketProfileDef> Profiles = new Dictionary<string, ValierMarketProfileDef>(StringComparer.OrdinalIgnoreCase);
        public static int RefreshMinutes = 180;

        public static string ConfigPath
        {
            get { return Path.Combine(Core.BaseDirectory, "Config", "ValierMarketVendors.cfg"); }
        }

        public static void Initialize()
        {
            LoadConfig();
            CommandSystem.Register("ValierVendorProfiles", AccessLevel.GameMaster, OnProfiles);
            CommandSystem.Register("PlaceValierVendor", AccessLevel.GameMaster, OnPlaceVendor);
            CommandSystem.Register("ValierVendorRefresh", AccessLevel.GameMaster, OnRefreshVendor);
            CommandSystem.Register("ValierVendorRemove", AccessLevel.GameMaster, OnRemoveVendor);
        }

        public static void LoadConfig()
        {
            Profiles.Clear();

            if (!File.Exists(ConfigPath))
            {
                LoadBuiltInFallback();
                return;
            }

            string[] lines;
            try { lines = File.ReadAllLines(ConfigPath); }
            catch { LoadBuiltInFallback(); return; }

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                if (raw == null) continue;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                int idx = line.IndexOf('=');
                if (idx <= 0) continue;

                string key = line.Substring(0, idx).Trim();
                string val = line.Substring(idx + 1).Trim();

                if (InsensitiveEquals(key, "RefreshMinutes"))
                {
                    int rm; if (Int32.TryParse(val, out rm) && rm > 0) RefreshMinutes = rm;
                    continue;
                }

                if (!key.StartsWith("Profile.", StringComparison.OrdinalIgnoreCase)) continue;
                string[] parts = key.Split('.');
                if (parts.Length < 3) continue;

                string profileId = parts[1];
                string field = parts[2];
                ValierMarketProfileDef profile = GetOrCreateProfile(profileId);

                if (InsensitiveEquals(field, "VendorName")) profile.VendorName = val;
                else if (InsensitiveEquals(field, "Title")) profile.Title = val;
                else if (InsensitiveEquals(field, "MaxEntries"))
                {
                    int maxEntries; if (Int32.TryParse(val, out maxEntries) && maxEntries > 0) profile.MaxEntries = maxEntries;
                }
                else if (InsensitiveEquals(field, "Entry"))
                {
                    ValierMarketEntryDef entry = ParseEntry(val);
                    if (entry != null) profile.Entries.Add(entry);
                }
            }

            if (Profiles.Count == 0) LoadBuiltInFallback();
        }

        public static ValierMarketProfileDef GetProfile(string id)
        {
            ValierMarketProfileDef profile;
            if (id != null && Profiles.TryGetValue(id, out profile)) return profile;
            return null;
        }

        public static List<string> GetProfileNames()
        {
            List<string> list = new List<string>();
            foreach (KeyValuePair<string, ValierMarketProfileDef> kv in Profiles) list.Add(kv.Key);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static ValierMarketProfileDef GetOrCreateProfile(string id)
        {
            ValierMarketProfileDef profile;
            if (!Profiles.TryGetValue(id, out profile))
            {
                profile = new ValierMarketProfileDef();
                profile.Id = id;
                profile.VendorName = "a market vendor";
                profile.Title = "the market vendor";
                profile.MaxEntries = 12;
                Profiles[id] = profile;
            }
            return profile;
        }

        private static ValierMarketEntryDef ParseEntry(string val)
        {
            string[] p = val.Split('|');
            if (p.Length < 6) return null;

            int minQty, maxQty, minPrice, maxPrice, weight;
            if (!Int32.TryParse(p[1], out minQty)) return null;
            if (!Int32.TryParse(p[2], out maxQty)) return null;
            if (!Int32.TryParse(p[3], out minPrice)) return null;
            if (!Int32.TryParse(p[4], out maxPrice)) return null;
            if (!Int32.TryParse(p[5], out weight)) return null;

            if (minQty < 1) minQty = 1;
            if (maxQty < minQty) maxQty = minQty;
            if (minPrice < 1) minPrice = 1;
            if (maxPrice < minPrice) maxPrice = minPrice;
            if (weight < 1) weight = 1;

            ValierMarketEntryDef e = new ValierMarketEntryDef();
            e.TypeName = p[0].Trim();
            e.MinQty = minQty; e.MaxQty = maxQty;
            e.MinPrice = minPrice; e.MaxPrice = maxPrice;
            e.Weight = weight;
            return e;
        }

        private static void AddEntries(ValierMarketProfileDef profile, string[] rows)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                ValierMarketEntryDef e = ParseEntry(rows[i]);
                if (e != null) profile.Entries.Add(e);
            }
        }

        private static bool InsensitiveEquals(string a, string b)
        {
            return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static void LoadBuiltInFallback()
        {
            Profiles.Clear();
            ValierMarketProfileDef p;

            p = GetOrCreateProfile("Resource");
            p.VendorName = "a resource broker"; p.Title = "the resource broker"; p.MaxEntries = 14;
            AddEntries(p, new string[] {
                "IronIngot|15|40|8|12|10","DullCopperIngot|10|25|16|26|5","ShadowIronIngot|10|25|18|30|5",
                "CopperIngot|10|25|20|34|5","BronzeIngot|8|20|24|40|4","GoldenIngot|8|20|30|48|4",
                "Board|20|60|5|8|8","OakBoard|12|30|12|18|5","Leather|15|45|6|10|8","Cloth|20|60|3|6|8",
                "Arrow|50|150|2|4|7","Bolt|50|150|2|4|7","Bottle|10|40|4|8|6","Bandage|30|100|2|4|9" });

            p = GetOrCreateProfile("Reagents");
            p.VendorName = "a reagent smuggler"; p.Title = "the reagent smuggler"; p.MaxEntries = 12;
            AddEntries(p, new string[] {
                "BlackPearl|15|60|5|8|10","Bloodmoss|15|60|5|8|10","Garlic|15|60|5|8|10","Ginseng|15|60|5|8|10",
                "MandrakeRoot|15|60|6|10|10","Nightshade|15|60|6|10|10","SulfurousAsh|15|60|5|8|10","SpidersSilk|15|60|5|8|10",
                "BatWing|8|30|9|16|6","GraveDust|8|30|9|16|6","DaemonBlood|8|30|10|18|5","NoxCrystal|8|30|10|18|5" });

            p = GetOrCreateProfile("Scrolls");
            p.VendorName = "an arcane surplus dealer"; p.Title = "the arcane surplus dealer"; p.MaxEntries = 14;
            AddEntries(p, new string[] {
                "ClumsyScroll|4|12|18|28|7","CreateFoodScroll|4|12|18|28|6","HealScroll|4|12|28|42|8","MagicArrowScroll|4|12|24|38|8",
                "FireballScroll|4|12|42|68|7","LightningScroll|3|10|65|95|6","RecallScroll|4|15|35|55|9","MarkScroll|3|8|70|110|5",
                "GateTravelScroll|2|6|120|180|4","ResurrectionScroll|2|6|140|220|4","ArchCureScroll|2|8|90|135|4","ArchProtectionScroll|2|8|90|135|4",
                "MagicReflectScroll|2|8|80|125|4","ExplosionScroll|2|8|95|145|4" });

            p = GetOrCreateProfile("Spellbooks");
            p.VendorName = "a spellbook curator"; p.Title = "the spellbook curator"; p.MaxEntries = 8;
            AddEntries(p, new string[] {
                "Spellbook|1|2|250|400|8","NecromancerSpellbook|1|2|550|850|4","BookOfChivalry|1|2|450|750|3",
                "BookOfBushido|1|2|550|900|3","BookOfNinjitsu|1|2|550|900|3","Runebook|1|3|350|600|5",
                "BlankScroll|20|80|4|8|7","RecallScroll|5|15|35|55|5" });

            p = GetOrCreateProfile("Weapons");
            p.VendorName = "an arms merchant"; p.Title = "the arms merchant"; p.MaxEntries = 12;
            AddEntries(p, new string[] {
                "Dagger|1|3|45|70|8","ShortSpear|1|2|120|180|5","Spear|1|2|145|220|5","WarFork|1|2|150|235|4",
                "Kryss|1|2|165|250|5","Katana|1|2|185|285|5","VikingSword|1|2|200|300|4","WarMace|1|2|180|280|4",
                "Maul|1|2|155|240|4","Bow|1|2|160|250|5","Crossbow|1|2|210|320|4","HeavyCrossbow|1|2|260|390|3" });

            p = GetOrCreateProfile("Armor");
            p.VendorName = "an armor broker"; p.Title = "the armor broker"; p.MaxEntries = 14;
            AddEntries(p, new string[] {
                "LeatherGorget|1|3|40|65|7","LeatherGloves|1|3|40|65|7","LeatherArms|1|3|55|85|7","LeatherChest|1|3|75|120|7",
                "LeatherLegs|1|3|60|95|7","StuddedChest|1|2|120|180|5","ChainChest|1|2|150|230|4","ChainLegs|1|2|135|210|4",
                "RingmailChest|1|2|150|230|4","PlateChest|1|2|240|360|3","PlateLegs|1|2|210|320|3","PlateGloves|1|2|130|210|3",
                "Buckler|1|2|50|90|4","HeaterShield|1|2|120|190|4" });

            p = GetOrCreateProfile("Adventure");
            p.VendorName = "an adventurer's exchange"; p.Title = "the adventurer's exchange"; p.MaxEntries = 14;
            AddEntries(p, new string[] {
                "Bandage|30|120|2|4|10","Bottle|10|40|4|8|5","LesserHealPotion|5|20|28|40|7","HealPotion|5|20|40|58|7",
                "GreaterHealPotion|4|15|58|82|6","LesserCurePotion|5|20|25|38|7","CurePotion|5|20|38|54|7","GreaterCurePotion|4|15|54|78|6",
                "LesserRefreshPotion|5|20|22|34|6","RefreshPotion|5|20|34|48|6","TotalRefreshPotion|4|15|52|75|5","Lockpick|10|40|8|14|6",
                "Torch|3|12|8|14|4","Lantern|2|8|14|22|4" });

            p = GetOrCreateProfile("Treasure");
            p.VendorName = "a treasure curator"; p.Title = "the treasure curator"; p.MaxEntries = 9;
            AddEntries(p, new string[] {
                "Amber|2|8|75|120|6","Amethyst|2|8|75|120|6","Citrine|2|8|75|120|6","Diamond|1|6|130|220|4",
                "Emerald|1|6|110|180|5","Ruby|1|6|115|190|5","Sapphire|1|6|110|180|5","StarSapphire|1|5|145|240|3","Tourmaline|1|6|110|180|5" });

            p = GetOrCreateProfile("Caravan");
            p.VendorName = "a wandering caravan trader"; p.Title = "the wandering caravan trader"; p.MaxEntries = 14;
            AddEntries(p, new string[] {
                "IronIngot|10|30|8|12|6","Board|15|40|5|8|6","BlackPearl|10|30|5|8|6","MandrakeRoot|10|30|6|10|6",
                "RecallScroll|3|10|35|55|6","GateTravelScroll|1|4|120|180|3","Spellbook|1|1|250|400|3","Runebook|1|2|350|600|3",
                "Bandage|30|100|2|4|6","GreaterHealPotion|4|12|58|82|5","Katana|1|2|185|285|3","Bow|1|2|160|250|3",
                "LeatherChest|1|2|75|120|3","Diamond|1|4|130|220|2" });
        }

        private static void OnProfiles(CommandEventArgs e)
        {
            List<string> names = GetProfileNames();
            e.Mobile.SendMessage(0x59, "Valier vendor profiles:");
            for (int i = 0; i < names.Count; i++) e.Mobile.SendMessage(0x59, "- {0}", names[i]);
        }

        private static void OnPlaceVendor(CommandEventArgs e)
        {
            if (e == null || e.Length < 1 || e.Arguments == null || e.Arguments.Length < 1)
            {
                e.Mobile.SendMessage(0x22, "Usage: [PlaceValierVendor <Profile>");
                return;
            }

            string profile = e.Arguments[0];
            if (GetProfile(profile) == null)
            {
                e.Mobile.SendMessage(0x22, "Unknown profile '{0}'. Use [ValierVendorProfiles.", profile);
                return;
            }

            e.Mobile.SendMessage(0x59, "Target the ground where you want to place a {0} vendor.", profile);
            e.Mobile.Target = new PlaceVendorTarget(profile);
        }

        private static void OnRefreshVendor(CommandEventArgs e)
        {
            e.Mobile.SendMessage(0x59, "Target a Valier market vendor to refresh its stock.");
            e.Mobile.Target = new RefreshVendorTarget();
        }

        private static void OnRemoveVendor(CommandEventArgs e)
        {
            e.Mobile.SendMessage(0x59, "Target a Valier market vendor to remove it.");
            e.Mobile.Target = new RemoveVendorTarget();
        }

        private sealed class PlaceVendorTarget : Target
        {
            private readonly string m_Profile;
            public PlaceVendorTarget(string profile) : base(12, true, TargetFlags.None) { m_Profile = profile; }
            protected override void OnTarget(Mobile from, object targeted)
            {
                IPoint3D p = targeted as IPoint3D;
                if (p == null) { from.SendMessage(0x22, "That is not a valid location."); return; }
                Map map = from.Map;
                if (map == null) return;
                Point3D loc = new Point3D(p);
                if (targeted is Item) loc.Z = ((Item)targeted).Z;
                else if (targeted is Mobile) loc.Z = ((Mobile)targeted).Z;
                ValierMarketVendor vendor = new ValierMarketVendor(m_Profile);
                vendor.MoveToWorld(loc, map);
                from.SendMessage(0x59, "Placed a Valier market vendor with profile '{0}'.", m_Profile);
            }
        }

        private sealed class RefreshVendorTarget : Target
        {
            public RefreshVendorTarget() : base(12, false, TargetFlags.None) { }
            protected override void OnTarget(Mobile from, object targeted)
            {
                ValierMarketVendor vendor = targeted as ValierMarketVendor;
                if (vendor == null || vendor.Deleted) { from.SendMessage(0x22, "That is not a Valier market vendor."); return; }
                vendor.GenerateStock(true);
                from.SendMessage(0x59, "Vendor refreshed: {0}.", vendor.ProfileId);
            }
        }

        private sealed class RemoveVendorTarget : Target
        {
            public RemoveVendorTarget() : base(12, false, TargetFlags.None) { }
            protected override void OnTarget(Mobile from, object targeted)
            {
                ValierMarketVendor vendor = targeted as ValierMarketVendor;
                if (vendor == null || vendor.Deleted) { from.SendMessage(0x22, "That is not a Valier market vendor."); return; }
                string profile = vendor.ProfileId;
                vendor.Delete();
                from.SendMessage(0x59, "Removed Valier market vendor ({0}).", profile);
            }
        }
    }

    public sealed class ValierMarketVendor : BaseVendor
    {
        private string m_ProfileId;
        private DateTime m_NextRefresh;
        private List<ValierVendorStockEntry> m_Stock;
        private SBValierMarket m_SBInfo;
        private readonly List<SBInfo> m_SBInfos = new List<SBInfo>();

        [Constructable]
        public ValierMarketVendor() : this("Resource") { }
        public ValierMarketVendor(string profileId) : base("market vendor")
        {
            m_ProfileId = profileId;
            m_Stock = new List<ValierVendorStockEntry>();
            InitProfileAppearance();
            GenerateStock(true);
        }
        public ValierMarketVendor(Serial serial) : base(serial) { }

        public string ProfileId { get { return m_ProfileId; } }
        public DateTime NextRefresh { get { return m_NextRefresh; } }
        public List<ValierVendorStockEntry> Stock { get { if (m_Stock == null) m_Stock = new List<ValierVendorStockEntry>(); return m_Stock; } }
        protected override List<SBInfo> SBInfos { get { return m_SBInfos; } }
        public override bool IsActiveVendor { get { return true; } }
        public override bool IsActiveBuyer { get { return false; } }
        public override NpcGuild NpcGuild { get { return NpcGuild.MerchantsGuild; } }
        public override VendorShoeType ShoeType { get { return VendorShoeType.Shoes; } }

        public override void InitSBInfo()
        {
            m_SBInfos.Clear();
            m_SBInfo = new SBValierMarket(this);
            m_SBInfos.Add(m_SBInfo);
        }

        public override void InitOutfit()
        {
            base.InitOutfit();
            ValierMarketCompat.ClearLayers(this);
            AddItem(new FancyShirt(Utility.RandomNeutralHue()));
            AddItem(new LongPants(Utility.RandomNeutralHue()));
            AddItem(new BodySash(Utility.RandomBlueHue()));
            AddItem(new Boots(Utility.RandomNeutralHue()));
        }

        public override void OnThink()
        {
            base.OnThink();
            if (!Deleted && DateTime.UtcNow >= m_NextRefresh) GenerateStock(false);
        }

        public void GenerateStock(bool force)
        {
            ValierMarketProfileDef profile = ValierMarketSystem.GetProfile(m_ProfileId);
            if (profile == null)
            {
                profile = ValierMarketSystem.GetProfile("Resource");
                m_ProfileId = (profile != null ? profile.Id : "Resource");
            }
            if (profile == null || profile.Entries == null || profile.Entries.Count == 0) return;
            if (!force && Stock.Count > 0 && DateTime.UtcNow < m_NextRefresh) return;

            InitProfileAppearance();
            Stock.Clear();

            List<ValierMarketEntryDef> pool = new List<ValierMarketEntryDef>(profile.Entries);
            int count = Math.Min(profile.MaxEntries, pool.Count);

            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                ValierMarketEntryDef chosen = PickWeighted(pool);
                if (chosen == null) break;
                pool.Remove(chosen);
                int qty = Utility.RandomMinMax(chosen.MinQty, chosen.MaxQty);
                int price = Utility.RandomMinMax(chosen.MinPrice, chosen.MaxPrice);
                if (qty < 1) qty = 1;
                if (price < 1) price = 1;
                Stock.Add(new ValierVendorStockEntry(chosen.TypeName, price, qty));
            }

            m_NextRefresh = DateTime.UtcNow + TimeSpan.FromMinutes(Math.Max(1, ValierMarketSystem.RefreshMinutes));
            if (m_SBInfo != null) m_SBInfo.Rebuild();
        }

        private void InitProfileAppearance()
        {
            ValierMarketProfileDef profile = ValierMarketSystem.GetProfile(m_ProfileId);
            if (profile == null) return;
            Name = profile.VendorName;
            Title = profile.Title;
        }

        private static ValierMarketEntryDef PickWeighted(List<ValierMarketEntryDef> pool)
        {
            if (pool == null || pool.Count == 0) return null;
            int total = 0;
            for (int i = 0; i < pool.Count; i++) total += (pool[i].Weight > 0 ? pool[i].Weight : 1);
            int roll = Utility.Random(total);
            for (int i = 0; i < pool.Count; i++)
            {
                int w = (pool[i].Weight > 0 ? pool[i].Weight : 1);
                if (roll < w) return pool[i];
                roll -= w;
            }
            return pool[pool.Count - 1];
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.Write((int)1);
            writer.Write(m_ProfileId);
            writer.Write(m_NextRefresh);
            writer.Write(Stock.Count);
            for (int i = 0; i < Stock.Count; i++)
            {
                writer.Write(Stock[i].TypeName);
                writer.Write(Stock[i].Price);
                writer.Write(Stock[i].Amount);
            }
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            int version = reader.ReadInt();
            m_ProfileId = reader.ReadString();
            m_NextRefresh = reader.ReadDateTime();
            int count = reader.ReadInt();
            m_Stock = new List<ValierVendorStockEntry>();
            for (int i = 0; i < count; i++)
            {
                ValierVendorStockEntry e = new ValierVendorStockEntry();
                e.TypeName = reader.ReadString();
                e.Price = reader.ReadInt();
                e.Amount = reader.ReadInt();
                m_Stock.Add(e);
            }
            if (m_Stock.Count == 0) GenerateStock(true);
            else InitProfileAppearance();
        }
    }

    public sealed class SBValierMarket : SBInfo
    {
        private readonly ValierMarketVendor m_Vendor;
        private List<GenericBuyInfo> m_BuyInfo;
        private IShopSellInfo m_SellInfo;

        public SBValierMarket(ValierMarketVendor vendor)
        {
            m_Vendor = vendor;
            m_BuyInfo = new List<GenericBuyInfo>();
            m_SellInfo = new InternalSellInfo();
            Rebuild();
        }

        public override IShopSellInfo SellInfo { get { return m_SellInfo; } }
        public override List<GenericBuyInfo> BuyInfo { get { return m_BuyInfo; } }

        public void Rebuild()
        {
            m_BuyInfo = new List<GenericBuyInfo>();
            if (m_Vendor == null || m_Vendor.Stock == null) return;
            for (int i = 0; i < m_Vendor.Stock.Count; i++)
            {
                ValierVendorStockEntry s = m_Vendor.Stock[i];
                Type t = ResolveType(s.TypeName);
                if (t == null) continue;
                Item probe = CreateItem(t);
                if (probe == null) continue;
                int itemID = probe.ItemID;
                int hue = probe.Hue;
                probe.Delete();
                try { m_BuyInfo.Add(new GenericBuyInfo(t, s.Price, s.Amount, itemID, hue)); }
                catch { }
            }
        }

        private static Type ResolveType(string typeName)
        {
            try { return ScriptCompiler.FindTypeByName(typeName); }
            catch { return null; }
        }

        private static Item CreateItem(Type t)
        {
            try
            {
                object o = Activator.CreateInstance(t);
                if (o is Item) return (Item)o;
            }
            catch { }
            return null;
        }

        private sealed class InternalSellInfo : GenericSellInfo { }
    }
}
