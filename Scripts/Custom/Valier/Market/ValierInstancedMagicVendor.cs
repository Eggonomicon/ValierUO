using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;

using Server;
using Server.Accounting;
using Server.Commands;
using Server.ContextMenus;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Targeting;

namespace Server.Custom.Valier.Market
{
    public sealed class ValierInstancedVendorStock
    {
        public Item Item;
        public int Price;
        public int Tier;
        public string Label;
        public bool Featured;
        public string Tag;

        public ValierInstancedVendorStock()
        {
        }

        public ValierInstancedVendorStock(Item item, int price, int tier, string label, bool featured, string tag)
        {
            Item = item;
            Price = price;
            Tier = tier;
            Label = label;
            Featured = featured;
            Tag = tag;
        }
    }

    public sealed class ValierInstancedVendorProfile
    {
        public string Id;
        public string VendorName;
        public string Title;
        public string Mode;
        public int StockCount;
        public int MinTier;
        public int MaxTier;
        public double MinPriceMultiplier;
        public double MaxPriceMultiplier;
    }

    public static class ValierInstancedVendorSystem
    {
        public static readonly Dictionary<string, ValierInstancedVendorProfile> Profiles =
            new Dictionary<string, ValierInstancedVendorProfile>(StringComparer.OrdinalIgnoreCase);

        public static readonly Dictionary<string, double> RegionMultipliers =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public static readonly Dictionary<string, string> RegionPreferredModes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static int RefreshHours = 24;
        public static bool RefreshOnWorldLoad = true;
        public static bool DeleteUnsoldStockOnRefresh = true;
        public static bool EnableRegionMarkups = false;

        public static int FeaturedCount = 2;
        public static bool FeaturedPreferHighTier = true;

        public static bool EnableHighTierBuyLimits = false;
        public static int HighTierThreshold = 3;
        public static int HighTierLimitPerAccountPerCycle = 1;

        public static bool EnableHighTierCooldowns = false;
        public static int HighTierCooldownHours = 24;

        public static bool EnablePurchaseLogging = true;

        public static string ConfigPath
        {
            get { return Path.Combine(Core.BaseDirectory, "Config", "ValierInstancedMagicVendors.cfg"); }
        }

        public static string PurchaseLogPath
        {
            get { return Path.Combine(Core.BaseDirectory, "Logs", "ValierInstancedVendorPurchases.log"); }
        }

        public static string StockReportDirectory
        {
            get { return Path.Combine(Core.BaseDirectory, "Logs", "ValierInstancedVendorReports"); }
        }

        public static void Initialize()
        {
            LoadConfig();

            CommandSystem.Register("ValierInstancedVendorProfiles", AccessLevel.GameMaster, OnProfiles);
            CommandSystem.Register("PlaceValierInstancedVendor", AccessLevel.GameMaster, OnPlace);
            CommandSystem.Register("ValierInstancedVendorRefresh", AccessLevel.GameMaster, OnRefresh);
            CommandSystem.Register("ValierInstancedVendorRemove", AccessLevel.GameMaster, OnRemove);
            CommandSystem.Register("ValierInstancedVendorSetRegion", AccessLevel.GameMaster, OnSetRegion);
            CommandSystem.Register("ValierInstancedVendorLogSummary", AccessLevel.GameMaster, OnLogSummary);
            CommandSystem.Register("ValierInstancedVendorExportStock", AccessLevel.GameMaster, OnExportStock);
        }

        public static void LoadConfig()
        {
            Profiles.Clear();
            RegionMultipliers.Clear();
            RegionPreferredModes.Clear();

            if (!File.Exists(ConfigPath))
            {
                LoadBuiltInFallback();
                return;
            }

            string[] lines;

            try
            {
                lines = File.ReadAllLines(ConfigPath);
            }
            catch
            {
                LoadBuiltInFallback();
                return;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];

                if (raw == null)
                    continue;

                string line = raw.Trim();

                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                int eq = line.IndexOf('=');

                if (eq <= 0)
                    continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (InsensitiveEquals(key, "RefreshHours"))
                {
                    int v;
                    if (Int32.TryParse(val, out v) && v > 0)
                        RefreshHours = v;
                    continue;
                }

                if (InsensitiveEquals(key, "RefreshOnWorldLoad"))
                {
                    bool b;
                    if (TryParseBool(val, out b))
                        RefreshOnWorldLoad = b;
                    continue;
                }

                if (InsensitiveEquals(key, "DeleteUnsoldStockOnRefresh"))
                {
                    bool b;
                    if (TryParseBool(val, out b))
                        DeleteUnsoldStockOnRefresh = b;
                    continue;
                }

                if (InsensitiveEquals(key, "EnableRegionMarkups"))
                {
                    bool b;
                    if (TryParseBool(val, out b))
                        EnableRegionMarkups = b;
                    continue;
                }

                if (InsensitiveEquals(key, "FeaturedCount"))
                {
                    int v;
                    if (Int32.TryParse(val, out v) && v >= 0)
                        FeaturedCount = v;
                    continue;
                }

                if (InsensitiveEquals(key, "FeaturedPreferHighTier"))
                {
                    bool b;
                    if (TryParseBool(val, out b))
                        FeaturedPreferHighTier = b;
                    continue;
                }

                if (InsensitiveEquals(key, "EnableHighTierBuyLimits"))
                {
                    bool b;
                    if (TryParseBool(val, out b))
                        EnableHighTierBuyLimits = b;
                    continue;
                }

                if (InsensitiveEquals(key, "HighTierThreshold"))
                {
                    int v;
                    if (Int32.TryParse(val, out v) && v > 0)
                        HighTierThreshold = v;
                    continue;
                }

                if (InsensitiveEquals(key, "HighTierLimitPerAccountPerCycle"))
                {
                    int v;
                    if (Int32.TryParse(val, out v) && v >= 0)
                        HighTierLimitPerAccountPerCycle = v;
                    continue;
                }

                if (InsensitiveEquals(key, "EnableHighTierCooldowns"))
                {
                    bool b;
                    if (TryParseBool(val, out b))
                        EnableHighTierCooldowns = b;
                    continue;
                }

                if (InsensitiveEquals(key, "HighTierCooldownHours"))
                {
                    int v;
                    if (Int32.TryParse(val, out v) && v >= 0)
                        HighTierCooldownHours = v;
                    continue;
                }

                if (InsensitiveEquals(key, "EnablePurchaseLogging"))
                {
                    bool b;
                    if (TryParseBool(val, out b))
                        EnablePurchaseLogging = b;
                    continue;
                }

                if (key.StartsWith("Region.", StringComparison.OrdinalIgnoreCase))
                {
                    string regionTag = key.Substring("Region.".Length).Trim();

                    if (regionTag.Length > 0)
                    {
                        double d;
                        if (Double.TryParse(val, out d) && d > 0)
                            RegionMultipliers[regionTag] = d;
                    }

                    continue;
                }

                if (key.StartsWith("RegionPreferredMode.", StringComparison.OrdinalIgnoreCase))
                {
                    string regionTag = key.Substring("RegionPreferredMode.".Length).Trim();

                    if (regionTag.Length > 0 && !String.IsNullOrEmpty(val))
                        RegionPreferredModes[regionTag] = val;

                    continue;
                }

                if (!key.StartsWith("Profile.", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = key.Split('.');

                if (parts.Length < 3)
                    continue;

                string profileId = parts[1];
                string field = parts[2];

                ValierInstancedVendorProfile profile = GetOrCreateProfile(profileId);

                if (InsensitiveEquals(field, "VendorName"))
                    profile.VendorName = val;
                else if (InsensitiveEquals(field, "Title"))
                    profile.Title = val;
                else if (InsensitiveEquals(field, "Mode"))
                    profile.Mode = val;
                else if (InsensitiveEquals(field, "StockCount"))
                {
                    int v;
                    if (Int32.TryParse(val, out v) && v > 0)
                        profile.StockCount = v;
                }
                else if (InsensitiveEquals(field, "MinTier"))
                {
                    int v;
                    if (Int32.TryParse(val, out v) && v > 0)
                        profile.MinTier = v;
                }
                else if (InsensitiveEquals(field, "MaxTier"))
                {
                    int v;
                    if (Int32.TryParse(val, out v) && v > 0)
                        profile.MaxTier = v;
                }
                else if (InsensitiveEquals(field, "MinPriceMultiplier"))
                {
                    double d;
                    if (Double.TryParse(val, out d) && d > 0)
                        profile.MinPriceMultiplier = d;
                }
                else if (InsensitiveEquals(field, "MaxPriceMultiplier"))
                {
                    double d;
                    if (Double.TryParse(val, out d) && d > 0)
                        profile.MaxPriceMultiplier = d;
                }
            }

            if (Profiles.Count == 0)
                LoadBuiltInFallback();
        }

        private static ValierInstancedVendorProfile GetOrCreateProfile(string id)
        {
            ValierInstancedVendorProfile p;

            if (!Profiles.TryGetValue(id, out p))
            {
                p = new ValierInstancedVendorProfile();
                p.Id = id;
                p.VendorName = "a relic broker";
                p.Title = "the relic broker";
                p.Mode = "Mixed";
                p.StockCount = 8;
                p.MinTier = 1;
                p.MaxTier = 3;
                p.MinPriceMultiplier = 1.0;
                p.MaxPriceMultiplier = 1.15;
                Profiles[id] = p;
            }

            return p;
        }

        public static ValierInstancedVendorProfile GetProfile(string id)
        {
            ValierInstancedVendorProfile p;

            if (id != null && Profiles.TryGetValue(id, out p))
                return p;

            return null;
        }

        public static List<string> GetProfileNames()
        {
            List<string> list = new List<string>();

            foreach (KeyValuePair<string, ValierInstancedVendorProfile> kv in Profiles)
                list.Add(kv.Key);

            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public static double GetRegionMultiplier(string regionTag)
        {
            if (!EnableRegionMarkups || String.IsNullOrEmpty(regionTag))
                return 1.0;

            double d;
            if (RegionMultipliers.TryGetValue(regionTag, out d) && d > 0)
                return d;

            return 1.0;
        }

        public static string GetPreferredMode(string regionTag)
        {
            if (String.IsNullOrEmpty(regionTag))
                return String.Empty;

            string mode;
            if (RegionPreferredModes.TryGetValue(regionTag, out mode))
                return mode ?? String.Empty;

            return String.Empty;
        }

        public static void LogPurchase(Mobile buyer, ValierInstancedMagicVendor vendor, ValierInstancedVendorStock stock, int price)
        {
            if (!EnablePurchaseLogging || buyer == null || vendor == null || stock == null || stock.Item == null)
                return;

            try
            {
                string logsDir = Path.GetDirectoryName(PurchaseLogPath);

                if (!Directory.Exists(logsDir))
                    Directory.CreateDirectory(logsDir);

                string acct = buyer.Account != null ? buyer.Account.ToString() : "(no account)";
                string line = String.Format(
                    "[{0}] Buyer={1} Account={2} Vendor={3}/{4} Region={5} Item={6} Tier={7} Label={8} Tag={9} Featured={10} Price={11}",
                    DateTime.UtcNow.ToString("u"),
                    SafeName(buyer),
                    acct,
                    vendor.Name,
                    vendor.ProfileId,
                    vendor.RegionTag,
                    ValierInstancedMagicVendor.GetDisplayName(stock.Item),
                    stock.Tier,
                    stock.Label,
                    stock.Tag,
                    stock.Featured,
                    price
                );

                File.AppendAllText(PurchaseLogPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }


        private static void OnLogSummary(CommandEventArgs e)
        {
            int days = 7;

            if (e != null && e.Arguments != null && e.Arguments.Length > 0)
            {
                int parsed;
                if (Int32.TryParse(e.Arguments[0], out parsed) && parsed > 0)
                    days = parsed;
            }

            if (!File.Exists(PurchaseLogPath))
            {
                e.Mobile.SendMessage(0x22, "No purchase log file found yet.");
                return;
            }

            DateTime cutoff = DateTime.UtcNow - TimeSpan.FromDays(days);
            int entries = 0;
            long totalGold = 0;

            Dictionary<string, int> itemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, long> itemGold = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> vendorCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string[] lines = File.ReadAllLines(PurchaseLogPath);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (String.IsNullOrEmpty(line))
                        continue;

                    DateTime ts;
                    if (!TryParseLogTimestamp(line, out ts))
                        continue;

                    if (ts < cutoff)
                        continue;

                    string item = ExtractBetween(line, "Item=", " Tier=");
                    string vendor = ExtractBetween(line, "Vendor=", " Region=");
                    string priceText = ExtractAfter(line, "Price=");

                    int price;
                    if (!Int32.TryParse(priceText, out price))
                        price = 0;

                    entries++;
                    totalGold += price;

                    if (!String.IsNullOrEmpty(item))
                    {
                        Increment(itemCounts, item, 1);
                        Increment(itemGold, item, price);
                    }

                    if (!String.IsNullOrEmpty(vendor))
                        Increment(vendorCounts, vendor, 1);
                }
            }
            catch
            {
                e.Mobile.SendMessage(0x22, "Failed to read purchase log.");
                return;
            }

            e.Mobile.SendMessage(0x59, "Valier vendor purchase summary ({0} day(s)):", days);
            e.Mobile.SendMessage(0x59, "Entries: {0} | Total Gold: {1:N0}", entries, totalGold);

            List<KeyValuePair<string, int>> topItems = SortByCount(itemCounts);
            List<KeyValuePair<string, int>> topVendors = SortByCount(vendorCounts);

            if (topItems.Count > 0)
            {
                e.Mobile.SendMessage(0x59, "Top items:");
                for (int i = 0; i < topItems.Count && i < 5; i++)
                {
                    long gold = 0;
                    itemGold.TryGetValue(topItems[i].Key, out gold);
                    e.Mobile.SendMessage(0x59, "- {0}: {1} sold, {2:N0} gold", topItems[i].Key, topItems[i].Value, gold);
                }
            }

            if (topVendors.Count > 0)
            {
                e.Mobile.SendMessage(0x59, "Top vendors/profiles:");
                for (int i = 0; i < topVendors.Count && i < 5; i++)
                    e.Mobile.SendMessage(0x59, "- {0}: {1} sale(s)", topVendors[i].Key, topVendors[i].Value);
            }
        }

        private static void OnExportStock(CommandEventArgs e)
        {
            e.Mobile.SendMessage(0x59, "Target an instanced vendor to export its stock report.");
            e.Mobile.Target = new ExportStockTarget();
        }

        private static bool TryParseLogTimestamp(string line, out DateTime value)
        {
            value = DateTime.MinValue;

            if (String.IsNullOrEmpty(line) || line[0] != '[')
                return false;

            int end = line.IndexOf(']');
            if (end <= 1)
                return false;

            string ts = line.Substring(1, end - 1);

            if (DateTime.TryParseExact(ts, "yyyy-MM-dd HH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
                return true;

            if (DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
                return true;

            return false;
        }

        private static string ExtractBetween(string line, string start, string end)
        {
            if (String.IsNullOrEmpty(line))
                return String.Empty;

            int s = line.IndexOf(start, StringComparison.OrdinalIgnoreCase);
            if (s < 0)
                return String.Empty;

            s += start.Length;
            int e = line.IndexOf(end, s, StringComparison.OrdinalIgnoreCase);
            if (e < 0)
                return line.Substring(s).Trim();

            return line.Substring(s, e - s).Trim();
        }

        private static string ExtractAfter(string line, string token)
        {
            if (String.IsNullOrEmpty(line))
                return String.Empty;

            int s = line.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (s < 0)
                return String.Empty;

            s += token.Length;
            return line.Substring(s).Trim();
        }

        private static void Increment(Dictionary<string, int> dict, string key, int amount)
        {
            int current;
            dict.TryGetValue(key, out current);
            dict[key] = current + amount;
        }

        private static void Increment(Dictionary<string, long> dict, string key, long amount)
        {
            long current;
            dict.TryGetValue(key, out current);
            dict[key] = current + amount;
        }

        private static List<KeyValuePair<string, int>> SortByCount(Dictionary<string, int> dict)
        {
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(dict);
            list.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                int c = b.Value.CompareTo(a.Value);
                if (c != 0)
                    return c;
                return String.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static string ExportVendorStockReport(ValierInstancedMagicVendor vendor)
        {
            if (vendor == null)
                return String.Empty;

            try
            {
                if (!Directory.Exists(StockReportDirectory))
                    Directory.CreateDirectory(StockReportDirectory);

                string fileName = String.Format("VendorStock_{0}_{1}_{2:yyyyMMdd_HHmmss}.txt",
                    SanitizeFilePart(vendor.ProfileId),
                    SanitizeFilePart(vendor.RegionTag),
                    DateTime.UtcNow);

                string fullPath = Path.Combine(StockReportDirectory, fileName);

                using (StreamWriter writer = new StreamWriter(fullPath, false))
                {
                    writer.WriteLine("Valier Instanced Vendor Stock Report");
                    writer.WriteLine("Generated (UTC): " + DateTime.UtcNow.ToString("u"));
                    writer.WriteLine("Vendor: " + vendor.Name + " " + vendor.Title);
                    writer.WriteLine("Profile: " + vendor.ProfileId);
                    writer.WriteLine("Region: " + vendor.RegionTag);
                    writer.WriteLine("Refresh Cycle: " + vendor.RefreshCycle);
                    writer.WriteLine("Next Refresh: " + vendor.NextRefresh.ToString("u"));
                    writer.WriteLine();

                    for (int i = 0; i < vendor.Stock.Count; i++)
                    {
                        ValierInstancedVendorStock s = vendor.Stock[i];
                        if (s == null || s.Item == null || s.Item.Deleted)
                            continue;

                        writer.WriteLine("#{0}: {1}", i, ValierInstancedMagicVendor.GetDisplayName(s.Item));
                        writer.WriteLine("  Price: {0}", s.Price.ToString("N0"));
                        writer.WriteLine("  Tier: {0}", s.Tier);
                        writer.WriteLine("  Label: {0}", s.Label);
                        writer.WriteLine("  Tag: {0}", s.Tag);
                        writer.WriteLine("  Featured: {0}", s.Featured);

                        List<string> details = ValierInstancedMagicVendor.BuildDetails(s.Item, s);
                        for (int d = 0; d < details.Count; d++)
                            writer.WriteLine("  - " + details[d]);

                        writer.WriteLine();
                    }
                }

                return fullPath;
            }
            catch
            {
                return String.Empty;
            }
        }

        private static string SanitizeFilePart(string s)
        {
            if (String.IsNullOrEmpty(s))
                return "None";

            foreach (char ch in Path.GetInvalidFileNameChars())
                s = s.Replace(ch, '_');

            return s.Replace(' ', '_');
        }

        private static string SafeName(Mobile m)
        {
            if (m == null)
                return "(null)";

            if (!String.IsNullOrEmpty(m.RawName))
                return m.RawName;

            if (!String.IsNullOrEmpty(m.Name))
                return m.Name;

            return m.Serial.ToString();
        }

        private static bool TryParseBool(string s, out bool value)
        {
            if (InsensitiveEquals(s, "true") || InsensitiveEquals(s, "yes") || InsensitiveEquals(s, "1"))
            {
                value = true;
                return true;
            }

            if (InsensitiveEquals(s, "false") || InsensitiveEquals(s, "no") || InsensitiveEquals(s, "0"))
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        private static void LoadBuiltInFallback()
        {
            Profiles.Clear();
            RegionMultipliers.Clear();
            RegionPreferredModes.Clear();

            AddProfile("MagicWeapons", "a mystic arms dealer", "the mystic arms dealer", "Weapons", 8, 1, 3, 1.00, 1.20);
            AddProfile("MagicArmor", "an enchanted armorer", "the enchanted armorer", "Armor", 8, 1, 3, 1.00, 1.20);
            AddProfile("MagicMixed", "a relic broker", "the relic broker", "Mixed", 10, 1, 3, 1.00, 1.25);
            AddProfile("Relics", "a relic curator", "the relic curator", "Signature", 6, 2, 3, 1.15, 1.35);
            AddProfile("Jewelry", "a jewel broker", "the jewel broker", "Jewelry", 8, 1, 3, 1.05, 1.25);
            AddProfile("Treasure", "a treasure dealer", "the treasure dealer", "Treasure", 8, 1, 3, 1.00, 1.20);

            EnableRegionMarkups = false;
            FeaturedCount = 2;
            FeaturedPreferHighTier = true;
            EnableHighTierBuyLimits = false;
            HighTierThreshold = 3;
            HighTierLimitPerAccountPerCycle = 1;
            EnableHighTierCooldowns = false;
            HighTierCooldownHours = 24;
            EnablePurchaseLogging = true;

            RegionMultipliers["Britain"] = 1.05;
            RegionMultipliers["Minoc"] = 1.10;
            RegionMultipliers["Yew"] = 1.08;
            RegionMultipliers["Moonglow"] = 1.12;

            RegionPreferredModes["Britain"] = "Weapons";
            RegionPreferredModes["Minoc"] = "Armor";
            RegionPreferredModes["Yew"] = "Treasure";
            RegionPreferredModes["Moonglow"] = "Jewelry";
        }

        private static void AddProfile(string id, string vendorName, string title, string mode, int stockCount, int minTier, int maxTier, double minMul, double maxMul)
        {
            ValierInstancedVendorProfile p = GetOrCreateProfile(id);
            p.VendorName = vendorName;
            p.Title = title;
            p.Mode = mode;
            p.StockCount = stockCount;
            p.MinTier = minTier;
            p.MaxTier = maxTier;
            p.MinPriceMultiplier = minMul;
            p.MaxPriceMultiplier = maxMul;
        }

        private static void OnProfiles(CommandEventArgs e)
        {
            List<string> names = GetProfileNames();

            e.Mobile.SendMessage(0x59, "Instanced vendor profiles:");
            for (int i = 0; i < names.Count; i++)
                e.Mobile.SendMessage(0x59, "- {0}", names[i]);
        }

        private static void OnPlace(CommandEventArgs e)
        {
            if (e == null || e.Length < 1 || e.Arguments == null || e.Arguments.Length < 1)
            {
                e.Mobile.SendMessage(0x22, "Usage: [PlaceValierInstancedVendor <Profile>");
                return;
            }

            string profile = e.Arguments[0];

            if (GetProfile(profile) == null)
            {
                e.Mobile.SendMessage(0x22, "Unknown profile '{0}'. Use [ValierInstancedVendorProfiles.", profile);
                return;
            }

            e.Mobile.SendMessage(0x59, "Target the ground where you want to place an instanced vendor ({0}).", profile);
            e.Mobile.Target = new PlaceTarget(profile);
        }

        private static void OnRefresh(CommandEventArgs e)
        {
            e.Mobile.SendMessage(0x59, "Target an instanced vendor to refresh its stock.");
            e.Mobile.Target = new RefreshTarget();
        }

        private static void OnRemove(CommandEventArgs e)
        {
            e.Mobile.SendMessage(0x59, "Target an instanced vendor to remove it.");
            e.Mobile.Target = new RemoveTarget();
        }

        private static void OnSetRegion(CommandEventArgs e)
        {
            if (e == null || e.Length < 1 || e.Arguments == null || e.Arguments.Length < 1)
            {
                e.Mobile.SendMessage(0x22, "Usage: [ValierInstancedVendorSetRegion <Tag>");
                return;
            }

            string regionTag = e.Arguments[0];
            e.Mobile.SendMessage(0x59, "Target an instanced vendor to set region tag '{0}'.", regionTag);
            e.Mobile.Target = new SetRegionTarget(regionTag);
        }

        private sealed class PlaceTarget : Target
        {
            private readonly string m_Profile;

            public PlaceTarget(string profile) : base(12, true, TargetFlags.None) { m_Profile = profile; }

            protected override void OnTarget(Mobile from, object targeted)
            {
                IPoint3D p = targeted as IPoint3D;

                if (p == null)
                {
                    from.SendMessage(0x22, "That is not a valid location.");
                    return;
                }

                Map map = from.Map;
                if (map == null)
                    return;

                Point3D loc = new Point3D(p);

                if (targeted is Item)
                    loc.Z = ((Item)targeted).Z;
                else if (targeted is Mobile)
                    loc.Z = ((Mobile)targeted).Z;

                ValierInstancedMagicVendor vendor = new ValierInstancedMagicVendor(m_Profile);
                vendor.MoveToWorld(loc, map);

                from.SendMessage(0x59, "Placed instanced vendor '{0}'.", m_Profile);
            }
        }

        private sealed class RefreshTarget : Target
        {
            public RefreshTarget() : base(12, false, TargetFlags.None) { }

            protected override void OnTarget(Mobile from, object targeted)
            {
                ValierInstancedMagicVendor vendor = targeted as ValierInstancedMagicVendor;

                if (vendor == null || vendor.Deleted)
                {
                    from.SendMessage(0x22, "That is not an instanced vendor.");
                    return;
                }

                vendor.GenerateStock(true);
                from.SendMessage(0x59, "Vendor refreshed: {0}.", vendor.ProfileId);
            }
        }

        private sealed class RemoveTarget : Target
        {
            public RemoveTarget() : base(12, false, TargetFlags.None) { }

            protected override void OnTarget(Mobile from, object targeted)
            {
                ValierInstancedMagicVendor vendor = targeted as ValierInstancedMagicVendor;

                if (vendor == null || vendor.Deleted)
                {
                    from.SendMessage(0x22, "That is not an instanced vendor.");
                    return;
                }

                string profile = vendor.ProfileId;
                vendor.Delete();
                from.SendMessage(0x59, "Removed instanced vendor ({0}).", profile);
            }
        }

        private sealed class SetRegionTarget : Target
        {
            private readonly string m_Tag;

            public SetRegionTarget(string tag) : base(12, false, TargetFlags.None) { m_Tag = tag; }

            protected override void OnTarget(Mobile from, object targeted)
            {
                ValierInstancedMagicVendor vendor = targeted as ValierInstancedMagicVendor;

                if (vendor == null || vendor.Deleted)
                {
                    from.SendMessage(0x22, "That is not an instanced vendor.");
                    return;
                }

                vendor.RegionTag = m_Tag;
                from.SendMessage(0x59, "Set vendor region tag to '{0}'.", m_Tag);
            }
        }

        private sealed class ExportStockTarget : Target
        {
            public ExportStockTarget() : base(12, false, TargetFlags.None) { }

            protected override void OnTarget(Mobile from, object targeted)
            {
                ValierInstancedMagicVendor vendor = targeted as ValierInstancedMagicVendor;

                if (vendor == null || vendor.Deleted)
                {
                    from.SendMessage(0x22, "That is not an instanced vendor.");
                    return;
                }

                string path = ValierInstancedVendorSystem.ExportVendorStockReport(vendor);

                if (String.IsNullOrEmpty(path))
                    from.SendMessage(0x22, "Failed to export vendor stock report.");
                else
                    from.SendMessage(0x59, "Exported vendor stock report to: {0}", path);
            }
        }

        private static bool InsensitiveEquals(string a, string b)
        {
            return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class ValierInstancedMagicFactory
    {
        private static readonly string[] WeaponTypeNames = new string[]
        {
            "Dagger","Kryss","Katana","VikingSword","Broadsword","Cutlass","WarMace","Maul",
            "Spear","ShortSpear","WarFork","Bow","Crossbow","HeavyCrossbow","Halberd","Axe","Longsword"
        };

        private static readonly string[] ArmorTypeNames = new string[]
        {
            "LeatherGorget","LeatherGloves","LeatherArms","LeatherChest","LeatherLegs",
            "StuddedGorget","StuddedGloves","StuddedArms","StuddedChest","StuddedLegs",
            "ChainCoif","ChainLegs","ChainChest","RingmailArms","RingmailGloves","RingmailLegs","RingmailChest",
            "PlateHelm","PlateGorget","PlateGloves","PlateArms","PlateChest","PlateLegs",
            "Buckler","BronzeShield","HeaterShield","MetalShield"
        };

        private static readonly string[] JewelryTypeNames = new string[]
        {
            "GoldRing","Ring","GoldBracelet","Bracelet","GoldNecklace","Necklace","GoldBeadNecklace","Beads"
        };

        private static readonly string[] GemTypeNames = new string[]
        {
            "Amber","Amethyst","Citrine","Diamond","Emerald","Ruby","Sapphire","StarSapphire","Tourmaline"
        };

        private static readonly string[] WeaponSignatureNames = new string[]
        {
            "Stormbite","Kingsfall","Dreadspike","Ashen Edge","Moonwake","Bloodsong","Iron Oath","Howling Fang"
        };

        private static readonly string[] ArmorSignatureNames = new string[]
        {
            "Lionguard","Soulglass","Night Banner","Ward of Dawn","Iron Veil","Winter Bastion","Dreadwall","Saint's Bulwark"
        };

        private static readonly string[] JewelrySignatureNames = new string[]
        {
            "Starfire Loop","Moonlit Oath","Crown of Echoes","Serpent Sigil","Dawn Tear","Ivory Whisper","Eclipse Chain","Fate's Promise"
        };

        private static readonly string[] TreasureSignatureNames = new string[]
        {
            "Cartographer's Secret","Vault of the Deep","Sailor's Fate","King's Ledger","Ember Atlas","Moon Cache","Sunken Testament","Warden's Hoard"
        };

        public static Item CreateStockItem(string mode, string vendorRegionTag, int tier, out int basePrice, out string label)
        {
            label = GetTierLabel(tier);
            string resolvedMode = ResolveMode(mode, vendorRegionTag);

            if (InsensitiveEquals(resolvedMode, "Weapons"))
                return CreateWeapon(tier, out basePrice);

            if (InsensitiveEquals(resolvedMode, "Armor"))
                return CreateArmor(tier, out basePrice);

            if (InsensitiveEquals(resolvedMode, "Jewelry"))
                return CreateJewelry(tier, out basePrice);

            if (InsensitiveEquals(resolvedMode, "Treasure"))
                return CreateTreasure(tier, out basePrice);

            if (InsensitiveEquals(resolvedMode, "Signature"))
            {
                label = "Signature";
                return CreateSignature(vendorRegionTag, tier, out basePrice);
            }

            if (Utility.RandomBool())
                return CreateWeapon(tier, out basePrice);

            return CreateArmor(tier, out basePrice);
        }

        private static string ResolveMode(string mode, string vendorRegionTag)
        {
            if (!InsensitiveEquals(mode, "Mixed"))
                return mode;

            string preferred = ValierInstancedVendorSystem.GetPreferredMode(vendorRegionTag);

            if (!String.IsNullOrEmpty(preferred) && Utility.RandomDouble() < 0.65)
                return preferred;

            // Normal mixed behavior
            double r = Utility.RandomDouble();

            if (r < 0.45)
                return "Weapons";
            if (r < 0.80)
                return "Armor";
            if (r < 0.90)
                return "Jewelry";

            return "Treasure";
        }

        public static string GetTierLabel(int tier)
        {
            if (tier <= 1)
                return "Lesser";
            if (tier == 2)
                return "Greater";
            return "Relic";
        }

        private static Item CreateWeapon(int tier, out int basePrice)
        {
            basePrice = 3000;

            BaseWeapon weapon = CreateRandomByNames(WeaponTypeNames) as BaseWeapon;
            if (weapon == null)
                weapon = new Longsword();

            weapon.LootType = LootType.Regular;
            weapon.Quality = WeaponQuality.Exceptional;

            if (tier <= 1)
            {
                weapon.DamageLevel = RandomFrom(new WeaponDamageLevel[] { WeaponDamageLevel.Ruin, WeaponDamageLevel.Might, WeaponDamageLevel.Force });
                weapon.AccuracyLevel = RandomFrom(new WeaponAccuracyLevel[] { WeaponAccuracyLevel.Accurate, WeaponAccuracyLevel.Surpassingly, WeaponAccuracyLevel.Eminently });
                weapon.DurabilityLevel = RandomFrom(new WeaponDurabilityLevel[] { WeaponDurabilityLevel.Durable, WeaponDurabilityLevel.Substantial, WeaponDurabilityLevel.Massive });
                weapon.Name = "a lesser enchanted " + GetBaseName(weapon);
                weapon.Hue = 1150;
                basePrice = Utility.RandomMinMax(2500, 4500);
            }
            else if (tier == 2)
            {
                weapon.DamageLevel = RandomFrom(new WeaponDamageLevel[] { WeaponDamageLevel.Force, WeaponDamageLevel.Power, WeaponDamageLevel.Vanq });
                weapon.AccuracyLevel = RandomFrom(new WeaponAccuracyLevel[] { WeaponAccuracyLevel.Eminently, WeaponAccuracyLevel.Exceedingly, WeaponAccuracyLevel.Supremely });
                weapon.DurabilityLevel = RandomFrom(new WeaponDurabilityLevel[] { WeaponDurabilityLevel.Massive, WeaponDurabilityLevel.Fortified, WeaponDurabilityLevel.Indestructible });
                weapon.Name = "an enchanted " + GetBaseName(weapon);
                weapon.Hue = 1161;
                basePrice = Utility.RandomMinMax(7000, 12000);
            }
            else
            {
                weapon.DamageLevel = WeaponDamageLevel.Vanq;
                weapon.AccuracyLevel = RandomFrom(new WeaponAccuracyLevel[] { WeaponAccuracyLevel.Exceedingly, WeaponAccuracyLevel.Supremely });
                weapon.DurabilityLevel = WeaponDurabilityLevel.Indestructible;
                weapon.Name = "a relic " + GetBaseName(weapon);
                weapon.Hue = 1175;
                basePrice = Utility.RandomMinMax(18000, 30000);

                double roll = Utility.RandomDouble();
                if (roll < 0.20)
                    weapon.Slayer = SlayerName.Repond;
                else if (roll < 0.40)
                    weapon.Slayer = SlayerName.Exorcism;
                else if (roll < 0.60)
                    weapon.Slayer = SlayerName.DragonSlaying;
            }

            return weapon;
        }

        private static Item CreateArmor(int tier, out int basePrice)
        {
            basePrice = 2500;

            Item item = CreateRandomByNames(ArmorTypeNames);
            BaseArmor armor = item as BaseArmor;
            BaseShield shield = item as BaseShield;

            if (armor != null)
            {
                armor.LootType = LootType.Regular;
                armor.Quality = ArmorQuality.Exceptional;

                if (tier <= 1)
                {
                    armor.ProtectionLevel = RandomFrom(new ArmorProtectionLevel[] { ArmorProtectionLevel.Defense, ArmorProtectionLevel.Guarding, ArmorProtectionLevel.Hardening });
                    armor.Durability = RandomFrom(new ArmorDurabilityLevel[] { ArmorDurabilityLevel.Durable, ArmorDurabilityLevel.Substantial, ArmorDurabilityLevel.Massive });
                    armor.Name = "a lesser enchanted " + GetBaseName(armor);
                    armor.Hue = 1150;
                    basePrice = Utility.RandomMinMax(2200, 4000);
                }
                else if (tier == 2)
                {
                    armor.ProtectionLevel = RandomFrom(new ArmorProtectionLevel[] { ArmorProtectionLevel.Hardening, ArmorProtectionLevel.Fortification, ArmorProtectionLevel.Invulnerability });
                    armor.Durability = RandomFrom(new ArmorDurabilityLevel[] { ArmorDurabilityLevel.Massive, ArmorDurabilityLevel.Fortified, ArmorDurabilityLevel.Indestructible });
                    armor.Name = "an enchanted " + GetBaseName(armor);
                    armor.Hue = 1166;
                    basePrice = Utility.RandomMinMax(6500, 11000);
                }
                else
                {
                    armor.ProtectionLevel = ArmorProtectionLevel.Invulnerability;
                    armor.Durability = ArmorDurabilityLevel.Indestructible;
                    armor.Name = "a relic " + GetBaseName(armor);
                    armor.Hue = 1170;
                    basePrice = Utility.RandomMinMax(17000, 28000);
                }

                return armor;
            }

            if (shield != null)
            {
                shield.LootType = LootType.Regular;
                shield.Name = (tier >= 3 ? "a relic " : tier == 2 ? "an enchanted " : "a lesser enchanted ") + GetBaseName(shield);
                shield.Hue = (tier >= 3 ? 1170 : tier == 2 ? 1166 : 1150);

                if (tier <= 1)
                    basePrice = Utility.RandomMinMax(1800, 3200);
                else if (tier == 2)
                    basePrice = Utility.RandomMinMax(5000, 9000);
                else
                    basePrice = Utility.RandomMinMax(13000, 22000);

                return shield;
            }

            basePrice = Utility.RandomMinMax(2200, 4000);
            return new LeatherChest();
        }

        private static Item CreateJewelry(int tier, out int basePrice)
        {
            basePrice = 2000;

            Item item = CreateRandomByNames(JewelryTypeNames);
            if (item == null)
                item = CreateRandomByNames(new string[] { "GoldRing", "Bracelet", "Necklace" });
            if (item == null)
                item = new GoldRing();

            item.LootType = LootType.Regular;
            item.Name = (tier <= 1 ? "a lesser enchanted " : tier == 2 ? "an enchanted " : "a relic ") + GetBaseName(item);
            item.Hue = (tier <= 1 ? 1153 : tier == 2 ? 1161 : 1175);

            BaseJewel jewel = item as BaseJewel;

            if (jewel != null)
            {
                if (tier <= 1)
                {
                    TrySetAttr(jewel, "BonusStr", Utility.RandomMinMax(1, 2));
                    TrySetAttr(jewel, "BonusDex", Utility.RandomMinMax(1, 2));
                    TrySetAttr(jewel, "BonusInt", Utility.RandomMinMax(1, 2));
                    basePrice = Utility.RandomMinMax(2500, 5000);
                }
                else if (tier == 2)
                {
                    TrySetAttr(jewel, "BonusStr", Utility.RandomMinMax(2, 4));
                    TrySetAttr(jewel, "BonusDex", Utility.RandomMinMax(2, 4));
                    TrySetAttr(jewel, "BonusInt", Utility.RandomMinMax(2, 4));
                    TrySetAttr(jewel, "Luck", Utility.RandomMinMax(20, 60));
                    basePrice = Utility.RandomMinMax(7000, 12000);
                }
                else
                {
                    TrySetAttr(jewel, "BonusStr", Utility.RandomMinMax(4, 6));
                    TrySetAttr(jewel, "BonusDex", Utility.RandomMinMax(4, 6));
                    TrySetAttr(jewel, "BonusInt", Utility.RandomMinMax(4, 6));
                    TrySetAttr(jewel, "Luck", Utility.RandomMinMax(80, 150));
                    TrySetAttr(jewel, "SpellDamage", Utility.RandomMinMax(2, 6));
                    basePrice = Utility.RandomMinMax(16000, 26000);
                }
            }
            else
            {
                basePrice = (tier <= 1 ? Utility.RandomMinMax(2500, 5000) : tier == 2 ? Utility.RandomMinMax(7000, 12000) : Utility.RandomMinMax(16000, 26000));
            }

            return item;
        }

        private static Item CreateTreasure(int tier, out int basePrice)
        {
            basePrice = 500;

            double roll = Utility.RandomDouble();

            if (roll < 0.35)
            {
                Item gem = CreateRandomByNames(GemTypeNames);
                if (gem == null)
                    gem = new Diamond();

                gem.Name = (tier <= 1 ? "a polished " : tier == 2 ? "an exquisite " : "a legendary ") + GetBaseName(gem);
                gem.Hue = (tier <= 1 ? 0 : tier == 2 ? 1153 : 1175);
                basePrice = (tier <= 1 ? Utility.RandomMinMax(150, 350) : tier == 2 ? Utility.RandomMinMax(500, 900) : Utility.RandomMinMax(1200, 2200));
                return gem;
            }

            if (roll < 0.65)
            {
                TreasureMap map = new TreasureMap(Math.Max(1, Math.Min(5, tier + Utility.RandomMinMax(0, 2))), Map.Felucca);
                map.Name = (tier <= 1 ? "a weathered treasure map" : tier == 2 ? "an intriguing treasure map" : "a legendary treasure map");
                basePrice = (tier <= 1 ? Utility.RandomMinMax(800, 1600) : tier == 2 ? Utility.RandomMinMax(2200, 4500) : Utility.RandomMinMax(6000, 11000));
                return map;
            }

            if (roll < 0.82)
            {
                Runebook rb = new Runebook();
                rb.Name = (tier <= 1 ? "a traveler's runebook" : tier == 2 ? "a seasoned runebook" : "a relic runebook");
                rb.Hue = (tier <= 1 ? 0 : tier == 2 ? 1153 : 1175);
                basePrice = (tier <= 1 ? Utility.RandomMinMax(500, 900) : tier == 2 ? Utility.RandomMinMax(1500, 2600) : Utility.RandomMinMax(4200, 7000));
                return rb;
            }

            Spellbook sb = new Spellbook();
            sb.Name = (tier <= 1 ? "a worn spellbook" : tier == 2 ? "an arcane spellbook" : "a relic spellbook");
            sb.Hue = (tier <= 1 ? 0 : tier == 2 ? 1161 : 1175);
            basePrice = (tier <= 1 ? Utility.RandomMinMax(350, 700) : tier == 2 ? Utility.RandomMinMax(1200, 2500) : Utility.RandomMinMax(3500, 6500));
            return sb;
        }

        private static Item CreateSignature(string vendorRegionTag, int tier, out int basePrice)
        {
            int useTier = (tier < 2 ? 2 : tier);
            string preferred = ValierInstancedVendorSystem.GetPreferredMode(vendorRegionTag);
            Item item = null;

            if (String.Equals(preferred, "Jewelry", StringComparison.OrdinalIgnoreCase))
                item = CreateJewelry(useTier, out basePrice);
            else if (String.Equals(preferred, "Treasure", StringComparison.OrdinalIgnoreCase))
                item = CreateTreasure(useTier, out basePrice);
            else if (String.Equals(preferred, "Armor", StringComparison.OrdinalIgnoreCase))
                item = CreateArmor(useTier, out basePrice);
            else
                item = CreateWeapon(useTier, out basePrice);

            if (item == null)
            {
                basePrice = 5000;
                return new GoldRing();
            }

            if (item is BaseWeapon)
                item.Name = WeaponSignatureNames[Utility.Random(WeaponSignatureNames.Length)];
            else if (item is BaseArmor || item is BaseShield)
                item.Name = ArmorSignatureNames[Utility.Random(ArmorSignatureNames.Length)];
            else if (item is BaseJewel)
                item.Name = JewelrySignatureNames[Utility.Random(JewelrySignatureNames.Length)];
            else
                item.Name = TreasureSignatureNames[Utility.Random(TreasureSignatureNames.Length)];

            item.Hue = 1175;
            basePrice = (int)Math.Round(basePrice * 1.25);
            return item;
        }

        private static Item CreateRandomByNames(string[] typeNames)
        {
            if (typeNames == null || typeNames.Length == 0)
                return null;

            for (int i = 0; i < 20; i++)
            {
                string typeName = typeNames[Utility.Random(typeNames.Length)];
                Type t = null;

                try
                {
                    t = ScriptCompiler.FindTypeByName(typeName);
                }
                catch
                {
                }

                if (t == null)
                    continue;

                try
                {
                    object o = Activator.CreateInstance(t);
                    if (o is Item)
                        return (Item)o;
                }
                catch
                {
                }
            }

            return null;
        }

        private static void TrySetAttr(BaseJewel jewel, string propertyName, int value)
        {
            if (jewel == null || value <= 0)
                return;

            try
            {
                object attrs = jewel.Attributes;
                if (attrs == null)
                    return;

                PropertyInfo pi = attrs.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);

                if (pi != null && pi.CanWrite)
                    pi.SetValue(attrs, value, null);
            }
            catch
            {
            }
        }

        private static T RandomFrom<T>(T[] list)
        {
            return list[Utility.Random(list.Length)];
        }

        private static string GetBaseName(Item item)
        {
            if (item == null)
                return "item";

            if (!String.IsNullOrEmpty(item.Name))
                return item.Name;

            return item.GetType().Name;
        }

        private static bool InsensitiveEquals(string a, string b)
        {
            return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    public class ValierInstancedMagicVendor : BaseCreature
    {
        private string m_ProfileId;
        private DateTime m_NextRefresh;
        private string m_RegionTag;
        private int m_RefreshCycle;
        private List<ValierInstancedVendorStock> m_Stock;
        private Dictionary<string, int> m_HighTierPurchaseCounts;
        private Dictionary<string, DateTime> m_HighTierCooldowns;

        [Constructable]
        public ValierInstancedMagicVendor() : this("MagicMixed")
        {
        }

        [Constructable]
        public ValierInstancedMagicVendor(string profileId)
            : base(AIType.AI_Vendor, FightMode.None, 10, 1, 0.2, 0.4)
        {
            m_ProfileId = profileId;
            m_RegionTag = String.Empty;
            m_Stock = new List<ValierInstancedVendorStock>();
            m_HighTierPurchaseCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            m_HighTierCooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            Blessed = true;
            CantWalk = true;
            Hue = Utility.RandomSkinHue();

            InitBody();
            InitOutfit();
            InitProfileAppearance();
            GenerateStock(true);
        }

        public ValierInstancedMagicVendor(Serial serial) : base(serial)
        {
        }

        public string ProfileId { get { return m_ProfileId; } }
        public DateTime NextRefresh { get { return m_NextRefresh; } }

        [CommandProperty(AccessLevel.GameMaster)]
        public string RegionTag
        {
            get { return m_RegionTag; }
            set { m_RegionTag = (value ?? String.Empty); }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public int RefreshCycle
        {
            get { return m_RefreshCycle; }
        }

        public List<ValierInstancedVendorStock> Stock
        {
            get
            {
                if (m_Stock == null)
                    m_Stock = new List<ValierInstancedVendorStock>();

                return m_Stock;
            }
        }

        public override bool IsInvulnerable { get { return true; } }
        public override bool CanTeach { get { return false; } }

        public void InitBody()
        {
            Female = Utility.RandomBool();
            Body = (Female ? 0x191 : 0x190);
        }

        public void InitOutfit()
        {
            ValierMarketCompat.ClearLayers(this);

            int themeHue = GetThemeHue();
            AddItem(new FancyShirt(themeHue));
            AddItem(new LongPants(Utility.RandomNeutralHue()));
            AddItem(new BodySash(themeHue));
            AddItem(new Boots(Utility.RandomNeutralHue()));
        }

        private void InitProfileAppearance()
        {
            ValierInstancedVendorProfile p = ValierInstancedVendorSystem.GetProfile(m_ProfileId);

            if (p == null)
                p = ValierInstancedVendorSystem.GetProfile("MagicMixed");

            if (p != null)
            {
                Name = p.VendorName;
                Title = p.Title;
            }
            else
            {
                Name = "a relic broker";
                Title = "the relic broker";
            }
        }

        public override void OnThink()
        {
            base.OnThink();

            if (!Deleted && DateTime.UtcNow >= m_NextRefresh)
                GenerateStock(false);
        }

        public override void OnDoubleClick(Mobile from)
        {
            if (from == null)
                return;

            if (!from.InRange(Location, 4))
            {
                from.SendLocalizedMessage(500446);
                return;
            }

            from.CloseGump(typeof(ValierInstancedVendorGump));
            from.SendGump(new ValierInstancedVendorGump(from, this, 0, "All", 0));
        }

        public override void GetContextMenuEntries(Mobile from, List<ContextMenuEntry> list)
        {
            base.GetContextMenuEntries(from, list);

            if (from != null && list != null)
                list.Add(new BrowseEntry(from, this));
        }

        private sealed class BrowseEntry : ContextMenuEntry
        {
            private readonly Mobile m_From;
            private readonly ValierInstancedMagicVendor m_Vendor;

            public BrowseEntry(Mobile from, ValierInstancedMagicVendor vendor) : base(6103, 4)
            {
                m_From = from;
                m_Vendor = vendor;
            }

            public override void OnClick()
            {
                if (m_From != null && m_Vendor != null && !m_Vendor.Deleted)
                    m_Vendor.OnDoubleClick(m_From);
            }
        }

        public void GenerateStock(bool force)
        {
            ValierInstancedVendorProfile profile = ValierInstancedVendorSystem.GetProfile(m_ProfileId);

            if (profile == null)
                profile = ValierInstancedVendorSystem.GetProfile("MagicMixed");

            if (profile == null)
                return;

            if (!force && Stock.Count > 0 && DateTime.UtcNow < m_NextRefresh)
                return;

            ClearStock(ValierInstancedVendorSystem.DeleteUnsoldStockOnRefresh);

            int count = Math.Max(1, profile.StockCount);
            double regionMul = ValierInstancedVendorSystem.GetRegionMultiplier(m_RegionTag);

            for (int i = 0; i < count; i++)
            {
                int tier = Utility.RandomMinMax(profile.MinTier, profile.MaxTier);
                int basePrice;
                string label;

                Item item = ValierInstancedMagicFactory.CreateStockItem(profile.Mode, m_RegionTag, tier, out basePrice, out label);

                if (item == null)
                    continue;

                item.Movable = true;
                item.Map = Map.Internal;

                double mul = profile.MinPriceMultiplier;

                if (profile.MaxPriceMultiplier > profile.MinPriceMultiplier)
                    mul = profile.MinPriceMultiplier + (Utility.RandomDouble() * (profile.MaxPriceMultiplier - profile.MinPriceMultiplier));

                int price = (int)Math.Max(1, Math.Round(basePrice * mul * regionMul));
                Stock.Add(new ValierInstancedVendorStock(item, price, tier, label, false, String.Empty));
            }

            MarkFeaturedStock();
            AssignTags();
            m_RefreshCycle++;
            m_HighTierPurchaseCounts.Clear();
            m_NextRefresh = DateTime.UtcNow + TimeSpan.FromHours(Math.Max(1, ValierInstancedVendorSystem.RefreshHours));
        }

        private void MarkFeaturedStock()
        {
            if (m_Stock == null || m_Stock.Count == 0 || ValierInstancedVendorSystem.FeaturedCount <= 0)
                return;

            for (int i = 0; i < m_Stock.Count; i++)
                m_Stock[i].Featured = false;

            int target = Math.Min(ValierInstancedVendorSystem.FeaturedCount, m_Stock.Count);

            List<int> candidates = new List<int>();

            for (int i = 0; i < m_Stock.Count; i++)
                candidates.Add(i);

            if (ValierInstancedVendorSystem.FeaturedPreferHighTier)
            {
                candidates.Sort(delegate(int x, int y)
                {
                    ValierInstancedVendorStock a = m_Stock[x];
                    ValierInstancedVendorStock b = m_Stock[y];

                    if (a.Tier != b.Tier)
                        return b.Tier.CompareTo(a.Tier);

                    if (String.Equals(a.Label, "Signature", StringComparison.OrdinalIgnoreCase) != String.Equals(b.Label, "Signature", StringComparison.OrdinalIgnoreCase))
                        return (String.Equals(a.Label, "Signature", StringComparison.OrdinalIgnoreCase) ? -1 : 1);

                    return Utility.Random(2) == 0 ? -1 : 1;
                });
            }

            int marked = 0;

            for (int i = 0; i < candidates.Count && marked < target; i++)
            {
                int idx = candidates[i];

                if (!m_Stock[idx].Featured)
                {
                    m_Stock[idx].Featured = true;
                    marked++;
                }
            }
        }


        private void AssignTags()
        {
            if (m_Stock == null)
                return;

            for (int i = 0; i < m_Stock.Count; i++)
            {
                ValierInstancedVendorStock s = m_Stock[i];

                if (s == null || s.Item == null)
                    continue;

                if (String.Equals(s.Label, "Signature", StringComparison.OrdinalIgnoreCase))
                    s.Tag = "Collector";
                else if (s.Item is TreasureMap || s.Item is Runebook || s.Item is Spellbook)
                    s.Tag = "Utility";
                else if (s.Item is BaseJewel && s.Tier >= 2)
                    s.Tag = "Collector";
                else if (s.Tier >= 3)
                    s.Tag = "Rare";
                else
                    s.Tag = String.Empty;
            }

            int newTags = 0;

            for (int i = 0; i < m_Stock.Count && newTags < 2; i++)
            {
                if (String.IsNullOrEmpty(m_Stock[i].Tag))
                {
                    m_Stock[i].Tag = "New";
                    newTags++;
                }
            }
        }

        public string GetFeaturedBannerHtml()
        {
            if (m_Stock == null || m_Stock.Count == 0)
                return "<BASEFONT COLOR=#FFD966>Featured picks rotate with each refresh.</BASEFONT>";

            List<string> names = new List<string>();

            for (int i = 0; i < m_Stock.Count; i++)
            {
                if (m_Stock[i].Featured && m_Stock[i].Item != null && !m_Stock[i].Item.Deleted)
                    names.Add(GetDisplayName(m_Stock[i].Item));
            }

            if (names.Count == 0)
                return "<BASEFONT COLOR=#FFD966>Featured picks rotate with each refresh.</BASEFONT>";

            string text = "Featured Picks: ";
            for (int i = 0; i < names.Count && i < 3; i++)
            {
                if (i > 0)
                    text += " • ";

                text += names[i];
            }

            return "<BASEFONT COLOR=#FFD966>" + text + "</BASEFONT>";
        }

        public string GetSpecialtyText()
        {
            ValierInstancedVendorProfile p = ValierInstancedVendorSystem.GetProfile(m_ProfileId);
            string mode = (p != null ? p.Mode : "Mixed");
            string preferred = ValierInstancedVendorSystem.GetPreferredMode(m_RegionTag);
            string specialty;

            if (String.Equals(mode, "Weapons", StringComparison.OrdinalIgnoreCase))
                specialty = "Specialty: enchanted weapons and battle-ready finds.";
            else if (String.Equals(mode, "Armor", StringComparison.OrdinalIgnoreCase))
                specialty = "Specialty: protective gear, shields, and armored relics.";
            else if (String.Equals(mode, "Jewelry", StringComparison.OrdinalIgnoreCase))
                specialty = "Specialty: enchanted jewelry, collector pieces, and curios.";
            else if (String.Equals(mode, "Treasure", StringComparison.OrdinalIgnoreCase))
                specialty = "Specialty: treasure maps, gems, travel tools, and curios.";
            else if (String.Equals(mode, "Signature", StringComparison.OrdinalIgnoreCase))
                specialty = "Specialty: named showcase relics and collector-grade stock.";
            else
                specialty = "Specialty: a mixed stock of relics, curios, and adventuring finds.";

            if (!String.IsNullOrEmpty(preferred) && (String.Equals(mode, "Mixed", StringComparison.OrdinalIgnoreCase) || String.Equals(mode, "Signature", StringComparison.OrdinalIgnoreCase)))
                specialty += " Regional bias: " + preferred + ".";

            return specialty;
        }

        private int GetThemeHue()
        {
            ValierInstancedVendorProfile p = ValierInstancedVendorSystem.GetProfile(m_ProfileId);
            string mode = (p != null ? p.Mode : "Mixed");

            if (String.Equals(mode, "Weapons", StringComparison.OrdinalIgnoreCase))
                return 2118;
            if (String.Equals(mode, "Armor", StringComparison.OrdinalIgnoreCase))
                return 2406;
            if (String.Equals(mode, "Jewelry", StringComparison.OrdinalIgnoreCase))
                return 1175;
            if (String.Equals(mode, "Treasure", StringComparison.OrdinalIgnoreCase))
                return 1153;
            if (String.Equals(mode, "Signature", StringComparison.OrdinalIgnoreCase))
                return 2213;

            return 1150;
        }

        private void ClearStock(bool deleteItems)
        {
            if (m_Stock == null)
                m_Stock = new List<ValierInstancedVendorStock>();

            for (int i = 0; i < m_Stock.Count; i++)
            {
                if (deleteItems && m_Stock[i] != null && m_Stock[i].Item != null && !m_Stock[i].Item.Deleted)
                    m_Stock[i].Item.Delete();
            }

            m_Stock.Clear();
        }

        public List<int> GetFilteredIndices(string filter, int sort)
        {
            List<int> list = new List<int>();

            for (int i = 0; i < Stock.Count; i++)
            {
                ValierInstancedVendorStock s = Stock[i];

                if (s == null || s.Item == null || s.Item.Deleted)
                    continue;

                if (MatchesFilter(s, filter))
                    list.Add(i);
            }

            if (sort == 1)
                list.Sort(new PriceAscComparer(this));
            else if (sort == 2)
                list.Sort(new PriceDescComparer(this));
            else if (sort == 3)
                list.Sort(new NameComparer(this));
            else
                list.Sort(new DefaultComparer(this));

            return list;
        }

        private bool MatchesFilter(ValierInstancedVendorStock stock, string filter)
        {
            if (String.IsNullOrEmpty(filter) || String.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
                return true;
            if (String.Equals(filter, "Featured", StringComparison.OrdinalIgnoreCase))
                return stock.Featured;
            if (String.Equals(filter, "Lesser", StringComparison.OrdinalIgnoreCase))
                return stock.Tier <= 1 && !String.Equals(stock.Label, "Signature", StringComparison.OrdinalIgnoreCase);
            if (String.Equals(filter, "Greater", StringComparison.OrdinalIgnoreCase))
                return stock.Tier == 2;
            if (String.Equals(filter, "Relic", StringComparison.OrdinalIgnoreCase))
                return stock.Tier >= 3 && !String.Equals(stock.Label, "Signature", StringComparison.OrdinalIgnoreCase);
            if (String.Equals(filter, "Signature", StringComparison.OrdinalIgnoreCase))
                return String.Equals(stock.Label, "Signature", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        public bool TryPurchase(Mobile buyer, int index)
        {
            if (buyer == null || buyer.Deleted)
                return false;

            if (!buyer.InRange(Location, 4))
            {
                buyer.SendLocalizedMessage(500446);
                return false;
            }

            if (index < 0 || index >= Stock.Count)
            {
                buyer.SendMessage(0x22, "That item is no longer available.");
                return false;
            }

            ValierInstancedVendorStock entry = Stock[index];

            if (entry == null || entry.Item == null || entry.Item.Deleted)
            {
                buyer.SendMessage(0x22, "That item is no longer available.");
                return false;
            }

            string acctKey = GetAccountKey(buyer);

            if (ValierInstancedVendorSystem.EnableHighTierBuyLimits &&
                entry.Tier >= ValierInstancedVendorSystem.HighTierThreshold &&
                ValierInstancedVendorSystem.HighTierLimitPerAccountPerCycle > 0)
            {
                int count = 0;

                if (!String.IsNullOrEmpty(acctKey))
                    m_HighTierPurchaseCounts.TryGetValue(acctKey, out count);

                if (count >= ValierInstancedVendorSystem.HighTierLimitPerAccountPerCycle)
                {
                    buyer.SendMessage(0x22, "You have reached the high-tier purchase limit for this vendor cycle.");
                    return false;
                }
            }

            if (ValierInstancedVendorSystem.EnableHighTierCooldowns &&
                entry.Tier >= ValierInstancedVendorSystem.HighTierThreshold &&
                ValierInstancedVendorSystem.HighTierCooldownHours > 0 &&
                !String.IsNullOrEmpty(acctKey))
            {
                DateTime until;
                if (m_HighTierCooldowns.TryGetValue(acctKey, out until) && DateTime.UtcNow < until)
                {
                    TimeSpan remain = until - DateTime.UtcNow;
                    buyer.SendMessage(0x22, "You must wait {0}h {1}m before buying another high-tier item here.", remain.Hours + (remain.Days * 24), remain.Minutes);
                    return false;
                }
            }

            int price = Math.Max(1, entry.Price);

            if (!ConsumePurchaseGold(buyer, price))
            {
                buyer.SendMessage(0x22, "You do not have enough gold in your backpack or bank.");
                return false;
            }

            Item item = entry.Item;
            Stock.RemoveAt(index);

            if (entry.Tier >= ValierInstancedVendorSystem.HighTierThreshold && !String.IsNullOrEmpty(acctKey))
            {
                if (ValierInstancedVendorSystem.EnableHighTierBuyLimits &&
                    ValierInstancedVendorSystem.HighTierLimitPerAccountPerCycle > 0)
                {
                    int count = 0;
                    m_HighTierPurchaseCounts.TryGetValue(acctKey, out count);
                    m_HighTierPurchaseCounts[acctKey] = count + 1;
                }

                if (ValierInstancedVendorSystem.EnableHighTierCooldowns &&
                    ValierInstancedVendorSystem.HighTierCooldownHours > 0)
                {
                    m_HighTierCooldowns[acctKey] = DateTime.UtcNow + TimeSpan.FromHours(ValierInstancedVendorSystem.HighTierCooldownHours);
                }
            }

            item.Movable = true;

            if (!buyer.AddToBackpack(item))
            {
                if (buyer.BankBox != null)
                    buyer.BankBox.DropItem(item);
                else
                    item.Delete();
            }

            ValierInstancedVendorSystem.LogPurchase(buyer, this, entry, price);

            buyer.SendMessage(0x59, "You purchase {0} for {1:N0} gold.", GetDisplayName(item), price);
            SayTo(buyer, "A fine choice.");
            return true;
        }

        private static string GetAccountKey(Mobile buyer)
        {
            if (buyer == null)
                return String.Empty;

            Account acc = buyer.Account as Account;

            if (acc != null && !String.IsNullOrEmpty(acc.Username))
                return acc.Username;

            return buyer.RawName ?? buyer.Name ?? String.Empty;
        }

        private static bool ConsumePurchaseGold(Mobile buyer, int amount)
        {
            if (buyer == null || amount <= 0)
                return false;

            Container pack = buyer.Backpack;

            if (pack != null)
            {
                try
                {
                    if (pack.GetAmount(typeof(Gold)) >= amount && pack.ConsumeTotal(typeof(Gold), amount))
                        return true;
                }
                catch
                {
                }
            }

            try
            {
                if (Banker.Withdraw(buyer, amount))
                    return true;
            }
            catch
            {
            }

            return false;
        }

        public static string GetDisplayName(Item item)
        {
            if (item == null)
                return "item";

            if (!String.IsNullOrEmpty(item.Name))
                return item.Name;

            return item.GetType().Name;
        }

        public static string GetTierTagHtml(ValierInstancedVendorStock stock)
        {
            if (stock == null)
                return "";

            string label = (String.IsNullOrEmpty(stock.Label) ? "Stock" : stock.Label);

            if (String.Equals(label, "Signature", StringComparison.OrdinalIgnoreCase))
                return "<BASEFONT COLOR=#E6C200>[Signature]</BASEFONT>";

            if (stock.Tier <= 1)
                return "<BASEFONT COLOR=#77AAFF>[" + label + "]</BASEFONT>";

            if (stock.Tier == 2)
                return "<BASEFONT COLOR=#8BE28B>[" + label + "]</BASEFONT>";

            return "<BASEFONT COLOR=#D98CFF>[" + label + "]</BASEFONT>";
        }

        public static string GetFeaturedHtml(ValierInstancedVendorStock stock)
        {
            if (stock == null || !stock.Featured)
                return "";

            return "<BASEFONT COLOR=#FFD966>[Featured]</BASEFONT>";
        }


        public static string GetTagHtml(ValierInstancedVendorStock stock)
        {
            if (stock == null || String.IsNullOrEmpty(stock.Tag))
                return "";

            if (String.Equals(stock.Tag, "Rare", StringComparison.OrdinalIgnoreCase))
                return "<BASEFONT COLOR=#FF9999>[Rare]</BASEFONT>";

            if (String.Equals(stock.Tag, "Collector", StringComparison.OrdinalIgnoreCase))
                return "<BASEFONT COLOR=#E6C200>[Collector]</BASEFONT>";

            if (String.Equals(stock.Tag, "Utility", StringComparison.OrdinalIgnoreCase))
                return "<BASEFONT COLOR=#80DFFF>[Utility]</BASEFONT>";

            return "<BASEFONT COLOR=#FFA64D>[New]</BASEFONT>";
        }

        public static List<string> BuildDetails(Item item, ValierInstancedVendorStock stock)
        {
            List<string> lines = new List<string>();

            if (item == null)
            {
                lines.Add("This item is no longer available.");
                return lines;
            }

            lines.Add("Type: " + item.GetType().Name);

            if (stock != null)
            {
                lines.Add("Rarity: " + (String.IsNullOrEmpty(stock.Label) ? "Stock" : stock.Label));
                if (stock.Featured)
                    lines.Add("Featured Stock");
            }

            BaseWeapon w = item as BaseWeapon;
            BaseArmor a = item as BaseArmor;
            BaseShield sh = item as BaseShield;
            BaseJewel j = item as BaseJewel;
            TreasureMap tm = item as TreasureMap;

            if (w != null)
            {
                lines.Add("Weapon Quality: " + w.Quality);
                lines.Add("Damage Level: " + w.DamageLevel);
                lines.Add("Accuracy Level: " + w.AccuracyLevel);
                lines.Add("Durability Level: " + w.DurabilityLevel);

                if (w.Slayer != SlayerName.None)
                    lines.Add("Slayer: " + w.Slayer);
            }
            else if (a != null)
            {
                lines.Add("Armor Quality: " + a.Quality);
                lines.Add("Protection Level: " + a.ProtectionLevel);
                lines.Add("Durability: " + a.Durability);
            }
            else if (sh != null)
            {
                lines.Add("Shield Type: " + sh.GetType().Name);
            }
            else if (j != null)
            {
                TryAddAttr(lines, j.Attributes, "BonusStr", "Bonus Str");
                TryAddAttr(lines, j.Attributes, "BonusDex", "Bonus Dex");
                TryAddAttr(lines, j.Attributes, "BonusInt", "Bonus Int");
                TryAddAttr(lines, j.Attributes, "Luck", "Luck");
                TryAddAttr(lines, j.Attributes, "SpellDamage", "Spell Damage");
            }
            else if (tm != null)
            {
                lines.Add("Treasure Map Level: " + tm.Level);
            }
            else if (item is Runebook)
            {
                lines.Add("Travel Utility Item");
            }
            else if (item is Spellbook)
            {
                lines.Add("Arcane Utility Item");
            }

            lines.Add("Hue: " + item.Hue);
            return lines;
        }

        private static void TryAddAttr(List<string> lines, object attrs, string propertyName, string label)
        {
            if (lines == null || attrs == null)
                return;

            try
            {
                PropertyInfo pi = attrs.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (pi == null || !pi.CanRead)
                    return;

                object v = pi.GetValue(attrs, null);

                if (v is int && (int)v > 0)
                    lines.Add(label + ": " + v.ToString());
            }
            catch
            {
            }
        }

        public override void OnDelete()
        {
            ClearStock(true);
            base.OnDelete();
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write((int)4);
            writer.Write(m_ProfileId);
            writer.Write(m_NextRefresh);
            writer.Write(m_RegionTag);
            writer.Write(m_RefreshCycle);

            writer.Write(Stock.Count);
            for (int i = 0; i < Stock.Count; i++)
            {
                writer.Write((Item)Stock[i].Item);
                writer.Write(Stock[i].Price);
                writer.Write(Stock[i].Tier);
                writer.Write(Stock[i].Label);
                writer.Write(Stock[i].Featured);
                writer.Write(Stock[i].Tag);
            }

            writer.Write(m_HighTierPurchaseCounts.Count);
            foreach (KeyValuePair<string, int> kv in m_HighTierPurchaseCounts)
            {
                writer.Write(kv.Key);
                writer.Write(kv.Value);
            }

            writer.Write(m_HighTierCooldowns.Count);
            foreach (KeyValuePair<string, DateTime> kv in m_HighTierCooldowns)
            {
                writer.Write(kv.Key);
                writer.Write(kv.Value);
            }
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);

            int version = reader.ReadInt();
            m_ProfileId = reader.ReadString();
            m_NextRefresh = reader.ReadDateTime();

            if (version >= 1)
                m_RegionTag = reader.ReadString();
            else
                m_RegionTag = String.Empty;

            if (version >= 2)
                m_RefreshCycle = reader.ReadInt();
            else
                m_RefreshCycle = 0;

            int count = reader.ReadInt();
            m_Stock = new List<ValierInstancedVendorStock>();

            for (int i = 0; i < count; i++)
            {
                Item item = reader.ReadItem();
                int price = reader.ReadInt();
                int tier = reader.ReadInt();
                string label = reader.ReadString();
                bool featured = (version >= 2 ? reader.ReadBool() : false);
                string tag = (version >= 4 ? reader.ReadString() : String.Empty);

                if (item != null && !item.Deleted)
                {
                    item.Map = Map.Internal;
                    m_Stock.Add(new ValierInstancedVendorStock(item, price, tier, label, featured, tag));
                }
            }

            m_HighTierPurchaseCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            m_HighTierCooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            if (version >= 2)
            {
                int buys = reader.ReadInt();

                for (int i = 0; i < buys; i++)
                {
                    string key = reader.ReadString();
                    int val = reader.ReadInt();

                    if (!String.IsNullOrEmpty(key))
                        m_HighTierPurchaseCounts[key] = val;
                }
            }

            if (version >= 3)
            {
                int cooldowns = reader.ReadInt();

                for (int i = 0; i < cooldowns; i++)
                {
                    string key = reader.ReadString();
                    DateTime val = reader.ReadDateTime();

                    if (!String.IsNullOrEmpty(key))
                        m_HighTierCooldowns[key] = val;
                }
            }

            InitProfileAppearance();

            if (ValierInstancedVendorSystem.RefreshOnWorldLoad)
                m_NextRefresh = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        }

        private sealed class DefaultComparer : IComparer<int>
        {
            private readonly ValierInstancedMagicVendor m_Vendor;

            public DefaultComparer(ValierInstancedMagicVendor vendor) { m_Vendor = vendor; }

            public int Compare(int x, int y)
            {
                ValierInstancedVendorStock a = m_Vendor.Stock[x];
                ValierInstancedVendorStock b = m_Vendor.Stock[y];

                if (a.Featured != b.Featured)
                    return (a.Featured ? -1 : 1);

                bool aSig = String.Equals(a.Label, "Signature", StringComparison.OrdinalIgnoreCase);
                bool bSig = String.Equals(b.Label, "Signature", StringComparison.OrdinalIgnoreCase);

                if (aSig != bSig)
                    return (aSig ? -1 : 1);

                if (a.Tier != b.Tier)
                    return b.Tier.CompareTo(a.Tier);

                return String.Compare(GetDisplayName(a.Item), GetDisplayName(b.Item), StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class PriceAscComparer : IComparer<int>
        {
            private readonly ValierInstancedMagicVendor m_Vendor;
            public PriceAscComparer(ValierInstancedMagicVendor vendor) { m_Vendor = vendor; }
            public int Compare(int x, int y) { return m_Vendor.Stock[x].Price.CompareTo(m_Vendor.Stock[y].Price); }
        }

        private sealed class PriceDescComparer : IComparer<int>
        {
            private readonly ValierInstancedMagicVendor m_Vendor;
            public PriceDescComparer(ValierInstancedMagicVendor vendor) { m_Vendor = vendor; }
            public int Compare(int x, int y) { return m_Vendor.Stock[y].Price.CompareTo(m_Vendor.Stock[x].Price); }
        }

        private sealed class NameComparer : IComparer<int>
        {
            private readonly ValierInstancedMagicVendor m_Vendor;
            public NameComparer(ValierInstancedMagicVendor vendor) { m_Vendor = vendor; }
            public int Compare(int x, int y)
            {
                return String.Compare(GetDisplayName(m_Vendor.Stock[x].Item), GetDisplayName(m_Vendor.Stock[y].Item), StringComparison.OrdinalIgnoreCase);
            }
        }
    }


    public class ValierInstancedVendorGump : Gump
    {
        private readonly Mobile m_From;
        private readonly ValierInstancedMagicVendor m_Vendor;
        private readonly int m_Page;
        private readonly string m_Filter;
        private readonly int m_Sort;
        private const int RowsPerPage = 8;

        public ValierInstancedVendorGump(Mobile from, ValierInstancedMagicVendor vendor, int page, string filter, int sort) : base(30, 25)
        {
            m_From = from;
            m_Vendor = vendor;
            m_Page = (page < 0 ? 0 : page);
            m_Filter = (String.IsNullOrEmpty(filter) ? "All" : filter);
            m_Sort = sort;

            Closable = true;
            Dragable = true;
            Resizable = false;

            AddPage(0);
            AddBackground(0, 0, 820, 520, 5054);
            AddLabel(20, 12, 1153, "Valier Instanced Vendor");
            AddLabel(20, 34, 88, vendor != null ? vendor.Name + " " + vendor.Title : "Vendor");
            AddHtml(20, 56, 770, 20, vendor != null ? vendor.GetSpecialtyText() : "", false, false);
            AddHtml(20, 78, 770, 22, vendor != null ? vendor.GetFeaturedBannerHtml() : "", false, false);
            AddLabel(20, 104, 88, vendor != null ? "Next refresh: " + vendor.NextRefresh.ToLocalTime().ToString() : "");
            AddLabel(300, 104, 88, "Filter: " + m_Filter + " | Sort: " + SortLabel(m_Sort));

            if (vendor != null && !String.IsNullOrEmpty(vendor.RegionTag))
                AddLabel(560, 104, 68, "Region: " + vendor.RegionTag);

            AddFilterButtons();
            AddSortButtons();

            AddLabel(20, 160, 1152, "Rarity");
            AddLabel(150, 160, 1152, "Tag");
            AddLabel(250, 160, 1152, "Item");
            AddLabel(585, 160, 1152, "Price");
            AddLabel(680, 160, 1152, "View");
            AddLabel(750, 160, 1152, "Buy");

            if (vendor == null || vendor.Deleted)
            {
                AddLabel(20, 190, 33, "This vendor is no longer available.");
                return;
            }

            List<int> filtered = vendor.GetFilteredIndices(m_Filter, m_Sort);

            int start = m_Page * RowsPerPage;
            int end = Math.Min(filtered.Count, start + RowsPerPage);
            int y = 188;

            for (int i = start; i < end; i++)
            {
                int stockIndex = filtered[i];
                ValierInstancedVendorStock s = vendor.Stock[stockIndex];
                string rarity = ValierInstancedMagicVendor.GetTierTagHtml(s);
                string featured = ValierInstancedMagicVendor.GetFeaturedHtml(s);
                string tag = ValierInstancedMagicVendor.GetTagHtml(s);
                string price = s.Price.ToString("N0");
                string name = ValierInstancedMagicVendor.GetDisplayName(s.Item);

                AddHtml(20, y, 120, 20, featured + " " + rarity, false, false);
                AddHtml(150, y, 85, 20, tag, false, false);
                AddHtml(250, y, 320, 20, name, false, false);
                AddLabel(585, y, 88, price);
                AddButton(690, y, 4005, 4007, 2000 + stockIndex, GumpButtonType.Reply, 0);
                AddButton(760, y, 4005, 4007, 1000 + stockIndex, GumpButtonType.Reply, 0);

                y += 36;
            }

            if (m_Page > 0)
            {
                AddButton(20, 480, 4014, 4016, 1, GumpButtonType.Reply, 0);
                AddLabel(55, 479, 88, "Previous");
            }

            if (end < filtered.Count)
            {
                AddButton(140, 480, 4005, 4007, 2, GumpButtonType.Reply, 0);
                AddLabel(175, 479, 88, "Next");
            }

            AddButton(720, 480, 4017, 4019, 3, GumpButtonType.Reply, 0);
            AddLabel(755, 479, 33, "Close");
        }

        private void AddFilterButtons()
        {
            AddButton(20, 128, 4005, 4007, 10, GumpButtonType.Reply, 0); AddLabel(50, 127, 88, "All");
            AddButton(90, 128, 4005, 4007, 11, GumpButtonType.Reply, 0); AddLabel(120, 127, 68, "Featured");
            AddButton(205, 128, 4005, 4007, 12, GumpButtonType.Reply, 0); AddLabel(235, 127, 88, "Lesser");
            AddButton(315, 128, 4005, 4007, 13, GumpButtonType.Reply, 0); AddLabel(345, 127, 88, "Greater");
            AddButton(425, 128, 4005, 4007, 14, GumpButtonType.Reply, 0); AddLabel(455, 127, 88, "Relic");
            AddButton(520, 128, 4005, 4007, 15, GumpButtonType.Reply, 0); AddLabel(550, 127, 1152, "Signature");
        }

        private void AddSortButtons()
        {
            AddButton(640, 128, 4005, 4007, 20, GumpButtonType.Reply, 0); AddLabel(670, 127, 88, "Default");
            AddButton(640, 146, 4005, 4007, 21, GumpButtonType.Reply, 0); AddLabel(670, 145, 88, "Price+");
            AddButton(710, 146, 4005, 4007, 22, GumpButtonType.Reply, 0); AddLabel(740, 145, 88, "Price-");
            AddButton(710, 128, 4005, 4007, 23, GumpButtonType.Reply, 0); AddLabel(740, 127, 88, "Name");
        }

        private static string SortLabel(int sort)
        {
            if (sort == 1) return "Price Asc";
            if (sort == 2) return "Price Desc";
            if (sort == 3) return "Name";
            return "Default";
        }

        public override void OnResponse(NetState sender, RelayInfo info)
        {
            if (m_From == null || m_From.Deleted || m_Vendor == null || m_Vendor.Deleted)
                return;

            int bid = info.ButtonID;

            if (bid == 0 || bid == 3)
                return;

            if (bid == 1)
            {
                int page = m_Page - 1;
                if (page < 0) page = 0;
                m_From.SendGump(new ValierInstancedVendorGump(m_From, m_Vendor, page, m_Filter, m_Sort));
                return;
            }

            if (bid == 2)
            {
                m_From.SendGump(new ValierInstancedVendorGump(m_From, m_Vendor, m_Page + 1, m_Filter, m_Sort));
                return;
            }

            if (bid >= 10 && bid <= 15)
            {
                string filter = "All";
                if (bid == 11) filter = "Featured";
                else if (bid == 12) filter = "Lesser";
                else if (bid == 13) filter = "Greater";
                else if (bid == 14) filter = "Relic";
                else if (bid == 15) filter = "Signature";

                m_From.SendGump(new ValierInstancedVendorGump(m_From, m_Vendor, 0, filter, m_Sort));
                return;
            }

            if (bid >= 20 && bid <= 23)
            {
                int sort = 0;
                if (bid == 21) sort = 1;
                else if (bid == 22) sort = 2;
                else if (bid == 23) sort = 3;

                m_From.SendGump(new ValierInstancedVendorGump(m_From, m_Vendor, 0, m_Filter, sort));
                return;
            }

            if (bid >= 2000)
            {
                int index = bid - 2000;
                if (index >= 0 && index < m_Vendor.Stock.Count)
                    m_From.SendGump(new ValierInstancedVendorInspectGump(m_From, m_Vendor, index, m_Page, m_Filter, m_Sort));
                return;
            }

            if (bid >= 1000)
            {
                int index = bid - 1000;
                m_Vendor.TryPurchase(m_From, index);
                m_From.SendGump(new ValierInstancedVendorGump(m_From, m_Vendor, m_Page, m_Filter, m_Sort));
            }
        }
    }

    public class ValierInstancedVendorInspectGump : Gump
    {
        private readonly Mobile m_From;
        private readonly ValierInstancedMagicVendor m_Vendor;
        private readonly int m_Index;
        private readonly int m_Page;
        private readonly string m_Filter;
        private readonly int m_Sort;

        public ValierInstancedVendorInspectGump(Mobile from, ValierInstancedMagicVendor vendor, int index, int page, string filter, int sort) : base(120, 80)
        {
            m_From = from;
            m_Vendor = vendor;
            m_Index = index;
            m_Page = page;
            m_Filter = filter;
            m_Sort = sort;

            Closable = true;
            Dragable = true;
            Resizable = false;

            AddPage(0);
            AddBackground(0, 0, 520, 380, 5054);
            AddLabel(20, 15, 1153, "Item Details");

            if (vendor == null || vendor.Deleted || index < 0 || index >= vendor.Stock.Count)
            {
                AddLabel(20, 50, 33, "That item is no longer available.");
                return;
            }

            ValierInstancedVendorStock stock = vendor.Stock[index];
            Item item = stock.Item;

            AddLabel(20, 42, 88, ValierInstancedMagicVendor.GetDisplayName(item));
            AddHtml(20, 68, 240, 20, ValierInstancedMagicVendor.GetFeaturedHtml(stock) + " " + ValierInstancedMagicVendor.GetTierTagHtml(stock) + " " + ValierInstancedMagicVendor.GetTagHtml(stock), false, false);
            AddLabel(320, 42, 88, "Price: " + stock.Price.ToString("N0"));

            List<string> lines = ValierInstancedMagicVendor.BuildDetails(item, stock);

            int y = 100;
            for (int i = 0; i < lines.Count && i < 12; i++)
            {
                AddHtml(20, y, 470, 20, lines[i], false, false);
                y += 22;
            }

            AddButton(20, 340, 4014, 4016, 1, GumpButtonType.Reply, 0);
            AddLabel(55, 339, 88, "Back");

            AddButton(380, 340, 4005, 4007, 2, GumpButtonType.Reply, 0);
            AddLabel(415, 339, 68, "Buy");
        }

        public override void OnResponse(NetState sender, RelayInfo info)
        {
            if (m_From == null || m_From.Deleted || m_Vendor == null || m_Vendor.Deleted)
                return;

            if (info.ButtonID == 1)
            {
                m_From.SendGump(new ValierInstancedVendorGump(m_From, m_Vendor, m_Page, m_Filter, m_Sort));
            }
            else if (info.ButtonID == 2)
            {
                m_Vendor.TryPurchase(m_From, m_Index);
                m_From.SendGump(new ValierInstancedVendorGump(m_From, m_Vendor, m_Page, m_Filter, m_Sort));
            }
        }
    }
}
