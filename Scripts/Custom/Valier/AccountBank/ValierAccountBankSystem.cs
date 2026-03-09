// ValierAccountBankSystem.cs
// ValierUO - Account Bank (Shared per account) - v6 "BankBox-style open"
// ServUO 57.x / C# 7.3 compatible
//
// Fixes for "opens then closes" and "doesn't open":
// - Uses the same open pattern as BankBox.Open():
//     EquipUpdate + DisplayTo
// - Keeps the container equipped (invisible) on a safe layer while in use, so the client knows the item exists.
// - Adds logout/disconnect handlers to detach + internalize the box, so it doesn't get stuck on a character.
//
// Commands:
// - [AccountBank  (alias: [ABank)
//
// Config: Config/ValierAccountBank.cfg
//
// Notes:
// - This uses a hidden container Item saved by world serialization.
// - The account stores the container Serial in an Account tag.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Server;
using Server.Accounting;
using Server.Commands;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.Valier
{
    public static class ValierAccountBankSystem
    {
        // Config
        public static bool Enabled = true;

        public static bool RequireNearBanker = true;
        public static int BankerRange = 3;

        public static bool RequireUnlockScroll = false;
        public static bool UnlockedByDefault = true;

        // If true, account bank auto-closes when you leave banker range (OSI feel)
        public static bool AutoCloseWhenOutOfRange = true;
        public static double AutoCloseCheckSeconds = 0.5;

        // Visuals
        public static int BoxItemID = 0xE7C;   // bank box
        public static int BoxHue = 0x489;      // orange-gold
        public static string BoxName = "Account Bank Box";

        // Which layer to use when equipping for client-known + opening.
        // Must be a layer that isn't used by normal player gear.
        public static Layer IntroLayer = Layer.ShopResale;

        public static string AccountTagSerial = "Valier.AccountBank.Serial";
        public static string AccountTagUnlocked = "Valier.AccountBank.Unlocked";

        public static string ConfigPath
        {
            get { return Path.Combine(Core.BaseDirectory, "Config", "ValierAccountBank.cfg"); }
        }

        private static readonly Dictionary<string, ValierAccountBankBox> _cache =
            new Dictionary<string, ValierAccountBankBox>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            LoadConfig();

            CommandSystem.Register("AccountBank", AccessLevel.Player, OnAccountBank);
            CommandSystem.Register("ABank", AccessLevel.Player, OnAccountBank);

            CommandSystem.Register("AccountBankReload", AccessLevel.GameMaster, e =>
            {
                LoadConfig();
                e.Mobile.SendMessage(0x59, "Valier AccountBank: config reloaded.");
            });

            // Detach/internlize on logout/disconnect so box doesn't get stuck on a character
            EventSink.Logout += OnLogout;
            EventSink.Disconnected += OnDisconnected;

            Console.WriteLine("[ValierAccountBank] v6 initialized. Enabled={0} RequireNearBanker={1} IntroLayer={2}",
                Enabled, RequireNearBanker, IntroLayer);
        }

        private static void OnAccountBank(CommandEventArgs e)
        {
            PlayerMobile pm = e.Mobile as PlayerMobile;

            if (pm != null)
                OpenFor(pm);
        }

        private static void OnLogout(LogoutEventArgs e)
        {
            PlayerMobile pm = e.Mobile as PlayerMobile;

            if (pm != null)
                DetachIfOwnedBy(pm);
        }

        private static void OnDisconnected(DisconnectedEventArgs e)
        {
            PlayerMobile pm = e.Mobile as PlayerMobile;

            if (pm != null)
                DetachIfOwnedBy(pm);
        }

        private static void DetachIfOwnedBy(PlayerMobile pm)
        {
            try
            {
                Account acc = pm.Account as Account;

                if (acc == null)
                    return;

                ValierAccountBankBox box = GetBox(acc, false);

                if (box != null && !box.Deleted)
                    box.DetachAndInternalize(pm);
            }
            catch { }
        }

        public static void OpenFor(PlayerMobile pm)
        {
            if (pm == null || pm.Deleted)
                return;

            if (!Enabled)
            {
                pm.SendMessage(0x22, "The account bank is currently disabled.");
                return;
            }

            if (RequireNearBanker && !IsNearBanker(pm, BankerRange))
            {
                pm.SendMessage(0x22, "You must be near a banker to access your account bank.");
                return;
            }

            Account acc = pm.Account as Account;

            if (acc == null)
            {
                pm.SendMessage(0x22, "No account found.");
                return;
            }

            if (!IsUnlocked(acc))
            {
                pm.SendMessage(0x22, "Your account bank is locked. Use an Account Bank scroll to unlock it.");
                return;
            }

            ValierAccountBankBox box = GetBox(acc, true);

            if (box == null || box.Deleted)
            {
                pm.SendMessage(0x22, "Account bank unavailable (failed to load).");
                return;
            }

            ApplyVisuals(box);

            // BankBox-style open: equip (invisible) -> EquipUpdate -> DisplayTo
            bool opened = box.OpenFor(pm, IntroLayer);

            if (opened && AutoCloseWhenOutOfRange && RequireNearBanker)
            {
                box.BeginAutoCloseMonitor(pm, BankerRange, AutoCloseCheckSeconds);
            }

            if (!opened)
                pm.SendMessage(0x22, "Account bank failed to open (see server console for warnings).");
        }

        public static void ApplyVisuals(ValierAccountBankBox box)
        {
            if (box == null || box.Deleted)
                return;

            try
            {
                box.ItemID = BoxItemID;
                box.Hue = BoxHue;

                if (!String.IsNullOrWhiteSpace(BoxName))
                    box.Name = BoxName;
            }
            catch { }
        }

        // Unlock logic
        public static bool IsUnlocked(Account acc)
        {
            if (acc == null)
                return false;

            if (!RequireUnlockScroll)
                return true;

            if (UnlockedByDefault)
                return true;

            string tag = acc.GetTag(AccountTagUnlocked);

            return String.Equals(tag, "true", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(tag, "1", StringComparison.OrdinalIgnoreCase);
        }

        public static void SetUnlocked(Account acc, bool value)
        {
            if (acc == null)
                return;

            acc.SetTag(AccountTagUnlocked, value ? "true" : "false");
        }

        // Storage + retrieval
        public static ValierAccountBankBox GetBox(Account acc, bool createIfMissing)
        {
            if (acc == null)
                return null;

            string key = acc.Username ?? "";
            if (key.Length == 0)
                key = acc.ToString();

            ValierAccountBankBox cached;

            if (_cache.TryGetValue(key, out cached))
            {
                if (cached != null && !cached.Deleted)
                    return cached;

                _cache.Remove(key);
            }

            ValierAccountBankBox box = null;

            Serial s;
            if (TryParseSerial(acc.GetTag(AccountTagSerial), out s))
            {
                Item it = World.FindItem(s);
                box = it as ValierAccountBankBox;

                if (box != null && box.Deleted)
                    box = null;
            }

            if (box == null && createIfMissing)
                box = CreateBox(acc);

            if (box != null)
                _cache[key] = box;

            return box;
        }

        private static ValierAccountBankBox CreateBox(Account acc)
        {
            try
            {
                ValierAccountBankBox box = new ValierAccountBankBox(acc.Username);

                // Keep it internalized until someone opens it
                box.Internalize();

                acc.SetTag(AccountTagSerial, box.Serial.Value.ToString(CultureInfo.InvariantCulture));

                return box;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryParseSerial(string s, out Serial serial)
        {
            serial = Serial.MinusOne;

            if (String.IsNullOrWhiteSpace(s))
                return false;

            int v;
            if (!Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return false;

            serial = (Serial)v;
            return true;
        }

        // Banker proximity
        public static bool IsNearBanker(PlayerMobile pm, int range)
        {
            if (pm == null || pm.Map == null)
                return false;

            try
            {
                foreach (Mobile m in pm.GetMobilesInRange(range))
                {
                    if (m == null || m.Deleted)
                        continue;

                    if (m is Banker)
                        return pm.InLOS(m);

                    BaseVendor v = m as BaseVendor;
                    if (v != null)
                    {
                        string title = v.Title ?? "";
                        if (title.IndexOf("bank", StringComparison.OrdinalIgnoreCase) >= 0)
                            return pm.InLOS(v);
                    }
                }
            }
            catch { }

            return false;
        }

        // Config
        public static void LoadConfig()
        {
            Enabled = true;
            RequireNearBanker = true;
            BankerRange = 3;

            RequireUnlockScroll = false;
            UnlockedByDefault = true;

            AutoCloseWhenOutOfRange = true;
            AutoCloseCheckSeconds = 0.5;

            BoxItemID = 0xE7C;
            BoxHue = 0x489;
            BoxName = "Account Bank Box";
            IntroLayer = Layer.ShopResale;

            try
            {
                if (!File.Exists(ConfigPath))
                    return;

                string[] lines = File.ReadAllLines(ConfigPath);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] ?? "";
                    int hash = line.IndexOf('#');
                    if (hash >= 0)
                        line = line.Substring(0, hash);

                    line = line.Trim();
                    if (line.Length == 0)
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();

                    if (k.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                        Enabled = ToBool(v, Enabled);
                    else if (k.Equals("RequireNearBanker", StringComparison.OrdinalIgnoreCase))
                        RequireNearBanker = ToBool(v, RequireNearBanker);
                    else if (k.Equals("BankerRange", StringComparison.OrdinalIgnoreCase))
                        BankerRange = Clamp(ToInt(v, BankerRange), 1, 12);

                    else if (k.Equals("RequireUnlockScroll", StringComparison.OrdinalIgnoreCase))
                        RequireUnlockScroll = ToBool(v, RequireUnlockScroll);
                    else if (k.Equals("UnlockedByDefault", StringComparison.OrdinalIgnoreCase))
                        UnlockedByDefault = ToBool(v, UnlockedByDefault);
                    else if (k.Equals("AutoCloseWhenOutOfRange", StringComparison.OrdinalIgnoreCase))
                        AutoCloseWhenOutOfRange = ToBool(v, AutoCloseWhenOutOfRange);
                    else if (k.Equals("AutoCloseCheckSeconds", StringComparison.OrdinalIgnoreCase))
                        AutoCloseCheckSeconds = ClampDouble(ToDouble(v, AutoCloseCheckSeconds), 0.2, 2.0);

                    else if (k.Equals("BoxItemID", StringComparison.OrdinalIgnoreCase))
                        BoxItemID = ToIntHexOrDec(v, BoxItemID);
                    else if (k.Equals("BoxHue", StringComparison.OrdinalIgnoreCase))
                        BoxHue = ToIntHexOrDec(v, BoxHue);
                    else if (k.Equals("BoxName", StringComparison.OrdinalIgnoreCase))
                        BoxName = v;
                    else if (k.Equals("IntroLayer", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Layer lay;
                            if (Enum.TryParse(v, true, out lay))
                                IntroLayer = lay;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static bool ToBool(string s, bool def)
        {
            if (String.IsNullOrWhiteSpace(s))
                return def;

            bool b;
            if (Boolean.TryParse(s, out b))
                return b;

            if (s == "1") return true;
            if (s == "0") return false;

            return def;
        }

        private static int ToInt(string s, int def)
        {
            int v;
            if (Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return v;

            return def;
        }

        private static double ToDouble(string s, double def)
        {
            double v;
            if (Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;

            return def;
        }

        private static double ClampDouble(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }


        private static int ToIntHexOrDec(string s, int def)
        {
            if (String.IsNullOrWhiteSpace(s))
                return def;

            s = s.Trim();

            try
            {
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return Int32.Parse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                if (s.Length > 2 && s.IndexOfAny(new[] { 'A','B','C','D','E','F','a','b','c','d','e','f' }) >= 0)
                    return Int32.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            catch { }

            return ToInt(s, def);
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
