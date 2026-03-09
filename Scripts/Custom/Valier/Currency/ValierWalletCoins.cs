using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

using Server;
using Server.Accounting;
using Server.Commands;
using Server.Items;
using Server.Mobiles;
using Server.Network;

namespace Server.Custom.ValierCurrency
{
    /*
     * Valier Wallet Coins (account-bound) — reviewed + command fix
     *
     * Persisted file:
     *   Saves/ValierWallet.xml
     *
     * XML format (example):
     *   <?xml version="1.0" encoding="utf-8"?>
     *   <ValierWallet>
     *     <acct key="someAccount" bal="250" />
     *     <acct key="otherAccount" bal="10" />
     *   </ValierWallet>
     *
     * Important:
     * - A file containing only: <ValierWallet /> is VALID XML; it just means "no balances were saved".
     *
     * Changes in this reviewed version:
     * - Adds a PLAYER command: [ValierWallet   (shows your wallet balance)
     * - Adds GM utility commands: [ValierWalletAdd / [ValierWalletSet / [ValierWalletDiag / [ValierWalletSave / [ValierWalletReload
     * - Keeps your legacy GM commands: [ValierCoins / [AddValierCoins / [SetValierCoins / [ReloadValierWallet
     *
     * Targeting notes (GM):
     * - You can target an ONLINE player by name (recommended).
     * - Or you can target an account username directly (works even if offline).
     *
     * Examples:
     *  [ValierWallet              -> (player) shows your balance
     *  [ValierWalletAdd 500       -> adds 500 to yourself
     *  [ValierWalletAdd Bob 100   -> adds 100 to Bob (if online) otherwise account "Bob"
     *  [ValierWalletSet owner 0   -> sets account "owner" to 0
     */

    public static class ValierWalletSystem
    {
        private static readonly object _sync = new object();

        // File path
        private static readonly string SavePath = Path.Combine(Core.BaseDirectory, "Saves", "ValierWallet.xml");

        // Account username (case-insensitive) -> balance
        private static readonly Dictionary<string, int> _balances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static bool _dirty;
        private static Timer _saveTimer;

        public static void Initialize()
        {
            // Load immediately so wallet is available even before WorldLoad.
            Load();

            EventSink.WorldLoad += OnWorldLoad;
            EventSink.WorldSave += OnWorldSave;

            // Player command
            CommandSystem.Register("ValierWallet", AccessLevel.Player, OnValierWallet);

            // GM commands (new)
            CommandSystem.Register("ValierWalletAdd", AccessLevel.GameMaster, OnValierWalletAdd);
            CommandSystem.Register("ValierWalletSet", AccessLevel.GameMaster, OnValierWalletSet);
            CommandSystem.Register("ValierWalletDiag", AccessLevel.GameMaster, OnValierWalletDiag);
            CommandSystem.Register("ValierWalletSave", AccessLevel.GameMaster, OnValierWalletSave);
            CommandSystem.Register("ValierWalletReload", AccessLevel.GameMaster, OnValierWalletReload);

            // Legacy GM commands (kept)
            CommandSystem.Register("ValierCoins", AccessLevel.GameMaster, OnValierCoins);
            CommandSystem.Register("AddValierCoins", AccessLevel.GameMaster, OnAddValierCoins);
            CommandSystem.Register("SetValierCoins", AccessLevel.GameMaster, OnSetValierCoins);
            CommandSystem.Register("ReloadValierWallet", AccessLevel.GameMaster, OnReloadWallet);
        }

        private static void OnWorldLoad()
        {
            // Safe: idempotent load
            Load();
        }

        private static void OnWorldSave(WorldSaveEventArgs e)
        {
            Save();
        }

        public static int GetBalance(Mobile m)
        {
            string key = GetAccountKey(m);

            if (string.IsNullOrEmpty(key))
                return 0;

            lock (_sync)
            {
                int bal;
                return _balances.TryGetValue(key, out bal) ? bal : 0;
            }
        }

        public static void SetBalance(Mobile m, int amount)
        {
            string key = GetAccountKey(m);

            if (string.IsNullOrEmpty(key))
                return;

            SetBalanceByKey(key, amount);
        }

        public static void Add(Mobile m, int amount)
        {
            if (m == null || m.Deleted || amount == 0)
                return;

            string key = GetAccountKey(m);

            if (string.IsNullOrEmpty(key))
                return;

            AddByKey(key, amount);
        }

        public static bool Consume(Mobile m, int amount)
        {
            if (m == null || m.Deleted || amount <= 0)
                return false;

            string key = GetAccountKey(m);

            if (string.IsNullOrEmpty(key))
                return false;

            lock (_sync)
            {
                int cur;
                if (!_balances.TryGetValue(key, out cur))
                    cur = 0;

                if (cur < amount)
                    return false;

                _balances[key] = cur - amount;
                MarkDirty_NoLock();
            }

            ScheduleSave();
            return true;
        }

        public static void SetBalanceByKey(string accountKey, int amount)
        {
            if (string.IsNullOrEmpty(accountKey))
                return;

            lock (_sync)
            {
                _balances[accountKey] = Math.Max(0, amount);
                MarkDirty_NoLock();
            }

            ScheduleSave();
        }

        public static void AddByKey(string accountKey, int amount)
        {
            if (string.IsNullOrEmpty(accountKey) || amount == 0)
                return;

            lock (_sync)
            {
                int cur;
                if (!_balances.TryGetValue(accountKey, out cur))
                    cur = 0;

                long next = (long)cur + (long)amount;
                if (next < 0) next = 0;
                if (next > int.MaxValue) next = int.MaxValue;

                _balances[accountKey] = (int)next;
                MarkDirty_NoLock();
            }

            ScheduleSave();
        }

        public static string GetAccountKey(Mobile m)
        {
            if (m == null)
                return null;

            IAccount acc = m.Account as IAccount;

            if (acc != null && !string.IsNullOrEmpty(acc.Username))
                return acc.Username;

            // Fallback: character serial (should be rare)
            return "char:" + m.Serial.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static void MarkDirty_NoLock()
        {
            _dirty = true;
        }

        private static void ScheduleSave()
        {
            lock (_sync)
            {
                if (!_dirty)
                    return;

                if (_saveTimer != null)
                    return;

                // Debounced save
                _saveTimer = Timer.DelayCall(TimeSpan.FromSeconds(5.0), new TimerCallback(SaveIfDirty));
            }
        }

        private static void SaveIfDirty()
        {
            lock (_sync)
            {
                _saveTimer = null;

                if (!_dirty)
                    return;
            }

            Save();
        }

        private static void EnsureSaveDirectory()
        {
            try
            {
                string dir = Path.GetDirectoryName(SavePath);

                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ValierWallet] ERROR ensuring save directory: {0}", ex);
            }
        }

        private static void Load()
        {
            lock (_sync)
            {
                _balances.Clear();
            }

            try
            {
                if (!File.Exists(SavePath))
                    return;

                XDocument doc = XDocument.Load(SavePath);

                XElement root = doc.Root;
                if (root == null)
                    return;

                // Accept either <ValierWallet> root or any other root.
                // Accept either <acct> or <account> element names.
                IEnumerable<XElement> entries = root.Elements("acct");

                bool any = false;
                foreach (XElement _ in entries) { any = true; break; }

                if (!any)
                    entries = root.Elements("account");

                int loaded = 0;

                foreach (XElement el in entries)
                {
                    XAttribute k = el.Attribute("key") ?? el.Attribute("username");
                    XAttribute v = el.Attribute("bal") ?? el.Attribute("balance");

                    if (k == null || v == null)
                        continue;

                    string key = k.Value;

                    int bal;
                    if (!int.TryParse(v.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out bal))
                        bal = 0;

                    if (!string.IsNullOrEmpty(key))
                    {
                        lock (_sync)
                        {
                            _balances[key] = Math.Max(0, bal);
                        }
                        loaded++;
                    }
                }

                lock (_sync)
                {
                    _dirty = false;
                }

                Console.WriteLine("[ValierWallet] Loaded {0} account balance(s) from {1}", loaded, SavePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ValierWallet] ERROR loading {0}: {1}", SavePath, ex);

                // Back up unreadable file so we don't overwrite it
                try
                {
                    if (File.Exists(SavePath))
                    {
                        string backup = SavePath + ".bad." + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                        File.Copy(SavePath, backup, true);
                        Console.WriteLine("[ValierWallet] Backed up unreadable wallet file to {0}", backup);
                    }
                }
                catch
                {
                    // ignore backup errors
                }

                lock (_sync)
                {
                    _balances.Clear();
                    _dirty = false;
                }
            }
        }

        private static void Save()
        {
            Dictionary<string, int> snapshot;

            lock (_sync)
            {
                snapshot = new Dictionary<string, int>(_balances, StringComparer.OrdinalIgnoreCase);
                _dirty = false;
            }

            try
            {
                EnsureSaveDirectory();

                XElement root = new XElement("ValierWallet");

                foreach (KeyValuePair<string, int> kv in snapshot)
                {
                    root.Add(new XElement("acct",
                        new XAttribute("key", kv.Key),
                        new XAttribute("bal", kv.Value.ToString(CultureInfo.InvariantCulture))
                    ));
                }

                XDocument doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);

                string tmp = SavePath + ".tmp";
                string bak = SavePath + ".bak";

                doc.Save(tmp);

                if (File.Exists(SavePath))
                {
                    // Prefer Replace, but fall back if it fails (e.g., some non-Windows file systems)
                    try
                    {
                        File.Replace(tmp, SavePath, bak);
                    }
                    catch
                    {
                        File.Copy(tmp, SavePath, true);
                        try { File.Delete(tmp); } catch { }
                    }
                }
                else
                {
                    File.Move(tmp, SavePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ValierWallet] ERROR saving {0}: {1}", SavePath, ex);

                // If saving failed, mark dirty again so we try next time
                lock (_sync)
                {
                    _dirty = true;
                }
            }
        }

        private static PlayerMobile FindOnlinePlayerByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            foreach (NetState ns in NetState.Instances)
            {
                if (ns == null || ns.Mobile == null)
                    continue;

                PlayerMobile pm = ns.Mobile as PlayerMobile;

                if (pm != null && pm.Name != null && pm.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return pm;
            }

            return null;
        }

        // Resolves target by:
        // 1) Online player name
        // 2) Account username (direct)
        private static string ResolveAccountKey(string playerOrAccountName, out PlayerMobile onlinePlayer)
        {
            onlinePlayer = null;

            if (string.IsNullOrEmpty(playerOrAccountName))
                return null;

            string s = playerOrAccountName.Trim();

            PlayerMobile pm = FindOnlinePlayerByName(s);
            if (pm != null)
            {
                onlinePlayer = pm;
                return GetAccountKey(pm);
            }

            // Not an online player; treat as account username directly
            return s;
        }

        #region Commands

        private static void OnValierWallet(CommandEventArgs e)
        {
            Mobile m = e.Mobile;

            if (m == null)
                return;

            string key = GetAccountKey(m);

            if (string.IsNullOrEmpty(key))
            {
                m.SendMessage(0x22, "No account was found for this character.");
                return;
            }

            m.SendMessage(0x59, "Valier Wallet: {0} coin(s).", GetBalance(m));
        }

        private static void OnValierWalletReload(CommandEventArgs e)
        {
            Load();
            e.Mobile.SendMessage(0x59, "ValierWallet: reloaded {0}", SavePath);
        }

        private static void OnValierWalletSave(CommandEventArgs e)
        {
            // Force save even if not dirty (useful for testing)
            lock (_sync)
            {
                _dirty = true;
            }

            Save();
            e.Mobile.SendMessage(0x59, "ValierWallet: saved {0}", SavePath);
        }

        private static void OnValierWalletDiag(CommandEventArgs e)
        {
            Mobile from = e.Mobile;

            if (from == null)
                return;

            string key = GetAccountKey(from);

            int entries;
            bool dirty;
            lock (_sync)
            {
                entries = _balances.Count;
                dirty = _dirty;
            }

            from.SendMessage(0x59, "ValierWallet path: {0}", SavePath);
            from.SendMessage(0x59, "Entries: {0}  Dirty: {1}  YourKey: {2}", entries, dirty, key ?? "(none)");
            from.SendMessage(0x59, "YourBalance: {0}", GetBalance(from));
        }

        private static void OnValierWalletAdd(CommandEventArgs e)
        {
            Mobile gm = e.Mobile;

            if (gm == null)
                return;

            if (e.Length == 1)
            {
                int amt = 0;
                try { amt = e.GetInt32(0); } catch { amt = 0; }

                if (amt == 0)
                {
                    gm.SendMessage(0x22, "Usage: [ValierWalletAdd <amount> OR [ValierWalletAdd <playerOrAccount> <amount>");
                    return;
                }

                Add(gm, amt);
                gm.SendMessage(0x59, "Added {0} Valier coin(s). New balance: {1}.", amt, GetBalance(gm));
                return;
            }

            if (e.Length < 2)
            {
                gm.SendMessage(0x22, "Usage: [ValierWalletAdd <amount> OR [ValierWalletAdd <playerOrAccount> <amount>");
                return;
            }

            string who = e.GetString(0);
            int amount = 0;
            try { amount = e.GetInt32(1); } catch { amount = 0; }

            if (amount == 0)
            {
                gm.SendMessage(0x22, "Amount must be non-zero.");
                return;
            }

            PlayerMobile online;
            string key = ResolveAccountKey(who, out online);

            if (string.IsNullOrEmpty(key))
            {
                gm.SendMessage(0x22, "Could not resolve target.");
                return;
            }

            AddByKey(key, amount);

            gm.SendMessage(0x59, "Added {0} coin(s) to {1}.", amount, online != null ? online.Name : ("account:" + key));

            if (online != null)
                online.SendMessage(0x59, "You received {0} Valier coin(s). New balance: {1}.", amount, GetBalance(online));
        }

        private static void OnValierWalletSet(CommandEventArgs e)
        {
            Mobile gm = e.Mobile;

            if (gm == null)
                return;

            if (e.Length == 1)
            {
                int amt = 0;
                try { amt = e.GetInt32(0); } catch { amt = 0; }

                if (amt < 0) amt = 0;

                SetBalance(gm, amt);
                gm.SendMessage(0x59, "Set your Valier wallet to {0}.", GetBalance(gm));
                return;
            }

            if (e.Length < 2)
            {
                gm.SendMessage(0x22, "Usage: [ValierWalletSet <amount> OR [ValierWalletSet <playerOrAccount> <amount>");
                return;
            }

            string who = e.GetString(0);
            int amount = 0;
            try { amount = e.GetInt32(1); } catch { amount = 0; }

            if (amount < 0) amount = 0;

            PlayerMobile online;
            string key = ResolveAccountKey(who, out online);

            if (string.IsNullOrEmpty(key))
            {
                gm.SendMessage(0x22, "Could not resolve target.");
                return;
            }

            SetBalanceByKey(key, amount);

            gm.SendMessage(0x59, "Set wallet for {0} to {1}.", online != null ? online.Name : ("account:" + key), amount);

            if (online != null)
                online.SendMessage(0x59, "Your Valier wallet balance is now {0}.", GetBalance(online));
        }

        // Legacy GM commands (kept)

        private static void OnReloadWallet(CommandEventArgs e)
        {
            Load();
            e.Mobile.SendMessage(0x59, "ValierWallet: reloaded {0}", SavePath);
        }

        private static void OnValierCoins(CommandEventArgs e)
        {
            Mobile from = e.Mobile;

            if (from == null)
                return;

            if (e.Length < 1)
            {
                from.SendMessage("Usage: [ValierCoins <playerOrAccount>");
                return;
            }

            PlayerMobile online;
            string key = ResolveAccountKey(e.GetString(0), out online);

            if (string.IsNullOrEmpty(key))
            {
                from.SendMessage("Target not found.");
                return;
            }

            int bal;
            lock (_sync)
            {
                _balances.TryGetValue(key, out bal);
            }

            from.SendMessage(0x59, "{0} has {1} Valier coin(s) (wallet).", online != null ? online.Name : ("account:" + key), bal);
        }

        private static void OnAddValierCoins(CommandEventArgs e)
        {
            Mobile from = e.Mobile;

            if (e.Length < 2)
            {
                from.SendMessage("Usage: [AddValierCoins <playerOrAccount> <amount>");
                return;
            }

            string who = e.GetString(0);

            int amt = 0;
            try { amt = e.GetInt32(1); }
            catch { amt = 0; }

            if (amt == 0)
            {
                from.SendMessage("Amount must be non-zero.");
                return;
            }

            PlayerMobile online;
            string key = ResolveAccountKey(who, out online);

            if (string.IsNullOrEmpty(key))
            {
                from.SendMessage("Target not found.");
                return;
            }

            AddByKey(key, amt);

            from.SendMessage(0x59, "Added {0} Valier coin(s) to {1}.", amt, online != null ? online.Name : ("account:" + key));

            if (online != null)
                online.SendMessage(0x59, "You received {0} Valier coin(s). New balance: {1}.", amt, GetBalance(online));
        }

        private static void OnSetValierCoins(CommandEventArgs e)
        {
            Mobile from = e.Mobile;

            if (e.Length < 2)
            {
                from.SendMessage("Usage: [SetValierCoins <playerOrAccount> <amount>");
                return;
            }

            string who = e.GetString(0);

            int amt = 0;
            try { amt = e.GetInt32(1); }
            catch { amt = 0; }

            if (amt < 0)
                amt = 0;

            PlayerMobile online;
            string key = ResolveAccountKey(who, out online);

            if (string.IsNullOrEmpty(key))
            {
                from.SendMessage("Target not found.");
                return;
            }

            SetBalanceByKey(key, amt);

            from.SendMessage(0x59, "Set {0}'s Valier wallet to {1}.", online != null ? online.Name : ("account:" + key), amt);

            if (online != null)
                online.SendMessage(0x59, "Your Valier wallet balance is now {0}.", GetBalance(online));
        }

        #endregion
    }

    /// <summary>
    /// Stackable voucher item: double-click to add (Amount) coins to wallet.
    ///
    /// - Because it's stackable, you can "split" the stack to redeem partial value.
    /// - The shop can create a single stack with Amount = value to act like a single-value token.
    /// </summary>
    public class ValierCoinVoucher : Item
    {
        [Constructable]
        public ValierCoinVoucher()
            : this(1)
        {
        }

        [Constructable]
        public ValierCoinVoucher(int amount)
            : base(0x14F0)
        {
            Name = "Valier coin voucher";
            Hue = 0x489;
            Stackable = true;

            // IMPORTANT: Amount is the value to redeem
            Amount = Math.Max(1, amount);

            Weight = 0.0;
            LootType = LootType.Regular;
        }

        public ValierCoinVoucher(Serial serial) : base(serial) { }

        public override void OnDoubleClick(Mobile from)
        {
            if (from == null || from.Deleted)
                return;

            if (!IsChildOf(from.Backpack))
            {
                from.SendMessage("That must be in your backpack to use it.");
                return;
            }

            int coins = Math.Max(1, Amount);

            ValierWalletSystem.Add(from, coins);

            from.SendMessage(0x59, "You redeem {0} Valier coin(s). New balance: {1}.", coins, ValierWalletSystem.GetBalance(from));

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
