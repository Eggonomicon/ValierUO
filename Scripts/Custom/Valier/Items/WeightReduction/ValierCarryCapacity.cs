using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Server;
using Server.Commands;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.ValierCarry
{
    /*
     * Valier Pure Carry Capacity (ServUO 57.x)
     *
     * What you asked for:
     * - "Pure carry capacity" bonuses (NOT STR)
     * - Wearables (sash/jewelry/etc.) that increase how much weight a player can carry
     * - Config-driven values
     *
     * This script provides:
     * 1) Interface ICarryCapacityBonus (items can implement it)
     * 2) ValierCarryCapacitySystem.GetBonus(Mobile) (sums equipped item bonuses)
     * 3) Example items:
     *      - ValierCarrySash   (BodySash)
     *      - ValierCarryRing   (GoldRing)
     *      - ValierCarryBracelet (GoldBracelet)
     *      - ValierCarryTalisman (GoldNecklace)
     * 4) Config: Config/ValierCarryCapacity.cfg
     * 5) GM: [ReloadValierCarryCapacity
     *
     * IMPORTANT:
     * - ServUO does not expose a "pure max weight bonus" hook by default.
     * - You must apply a tiny patch to either PlayerMobile.cs OR WeightOverloading.cs
     *   to add: + ValierCarryCapacitySystem.GetBonus(this)
     * - A ready-to-follow patch guide is included in the docs file I generated separately.
     */

    public interface ICarryCapacityBonus
    {
        int CarryCapacityBonus { get; }
    }

    public static class ValierCarryCapacityConfig
    {
        private static readonly object _sync = new object();
        private static readonly Dictionary<string, string> _kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string ConfigFilePath
        {
            get { return Path.Combine(Core.BaseDirectory, "Config", "ValierCarryCapacity.cfg"); }
        }

        public static void Reload()
        {
            lock (_sync)
            {
                _kv.Clear();

                try
                {
                    if (!File.Exists(ConfigFilePath))
                        return;

                    string[] raw = File.ReadAllLines(ConfigFilePath);

                    for (int i = 0; i < raw.Length; i++)
                    {
                        string line = raw[i];

                        if (line == null)
                            continue;

                        line = line.Trim();

                        if (line.Length == 0)
                            continue;

                        if (line.StartsWith("#") || line.StartsWith("//"))
                            continue;

                        int eq = line.IndexOf('=');

                        if (eq <= 0)
                            continue;

                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();

                        if (key.Length == 0)
                            continue;

                        _kv[key] = val;
                    }
                }
                catch
                {
                    _kv.Clear();
                }
            }
        }

        private static string GetString(string key, string def)
        {
            lock (_sync)
            {
                string v;
                if (_kv.TryGetValue(key, out v) && !string.IsNullOrWhiteSpace(v))
                    return v;

                return def;
            }
        }

        private static bool GetBool(string key, bool def)
        {
            string s = GetString(key, null);

            if (string.IsNullOrWhiteSpace(s))
                return def;

            bool b;
            if (bool.TryParse(s, out b))
                return b;

            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n != 0;

            return def;
        }

        private static int GetInt(string key, int def)
        {
            string s = GetString(key, null);

            if (string.IsNullOrWhiteSpace(s))
                return def;

            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;

            return def;
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static bool Enabled { get { return GetBool("ValierCarry.Enabled", true); } }
        public static bool Blessed { get { return GetBool("ValierCarry.Blessed", true); } }

        // These are "stones" of extra carry capacity (added to MaxWeight)
        public static int SashBonus { get { return Clamp(GetInt("ValierCarry.SashBonus", 50), 0, 10000); } }
        public static int RingBonus { get { return Clamp(GetInt("ValierCarry.RingBonus", 25), 0, 10000); } }
        public static int BraceletBonus { get { return Clamp(GetInt("ValierCarry.BraceletBonus", 25), 0, 10000); } }
        public static int TalismanBonus { get { return Clamp(GetInt("ValierCarry.TalismanBonus", 50), 0, 10000); } }
    }

    public static class ValierCarryCapacityCommands
    {
        public static void Initialize()
        {
            ValierCarryCapacityConfig.Reload();
            CommandSystem.Register("ReloadValierCarryCapacity", AccessLevel.GameMaster, OnReload);
        }

        private static void OnReload(CommandEventArgs e)
        {
            ValierCarryCapacityConfig.Reload();
            e.Mobile.SendMessage(0x59, "ValierCarry: reloaded Config/ValierCarryCapacity.cfg");
        }
    }

    public static class ValierCarryCapacitySystem
    {
        public static int GetBonus(Mobile m)
        {
            if (!ValierCarryCapacityConfig.Enabled || m == null || m.Deleted)
                return 0;

            int bonus = 0;

            // Equipped items are in m.Items
            try
            {
                foreach (Item item in m.Items)
                {
                    if (item == null || item.Deleted)
                        continue;

                    if (item is ICarryCapacityBonus b)
                    {
                        bonus += Math.Max(0, b.CarryCapacityBonus);
                        continue;
                    }

                    // Optional reflection fallback if you ever add other custom items:
                    // public int CarryCapacityBonus { get; }
                    try
                    {
                        var p = item.GetType().GetProperty("CarryCapacityBonus");
                        if (p != null && p.PropertyType == typeof(int))
                        {
                            object v = p.GetValue(item, null);
                            if (v is int n && n > 0)
                                bonus += n;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
                // Never break movement/weight checks
                return 0;
            }

            // Safety cap (optional): avoid extreme values from misconfigured items
            if (bonus < 0)
                bonus = 0;

            return bonus;
        }
    }

    // =========================
    // Example items (config-driven bonuses)
    // =========================

    public class ValierCarrySash : BodySash, ICarryCapacityBonus
    {
        [Constructable]
        public ValierCarrySash()
        {
            Name = "a porter’s sash";
            Hue = 0x489;

            if (ValierCarryCapacityConfig.Blessed)
                LootType = LootType.Blessed;
        }

        public ValierCarrySash(Serial serial) : base(serial) { }

        public int CarryCapacityBonus
        {
            get { return ValierCarryCapacityConfig.SashBonus; }
        }

        public override void GetProperties(ObjectPropertyList list)
        {
            base.GetProperties(list);

            if (ValierCarryCapacityConfig.Enabled)
                list.Add(1060658, "Carry Capacity\t+{0}", CarryCapacityBonus); // custom tooltip format
        }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }

    public class ValierCarryRing : GoldRing, ICarryCapacityBonus
    {
        [Constructable]
        public ValierCarryRing()
        {
            Name = "a porter’s ring";
            Hue = 0x489;

            if (ValierCarryCapacityConfig.Blessed)
                LootType = LootType.Blessed;
        }

        public ValierCarryRing(Serial serial) : base(serial) { }

        public int CarryCapacityBonus
        {
            get { return ValierCarryCapacityConfig.RingBonus; }
        }

        public override void GetProperties(ObjectPropertyList list)
        {
            base.GetProperties(list);

            if (ValierCarryCapacityConfig.Enabled)
                list.Add(1060658, "Carry Capacity\t+{0}", CarryCapacityBonus);
        }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }

    public class ValierCarryBracelet : GoldBracelet, ICarryCapacityBonus
    {
        [Constructable]
        public ValierCarryBracelet()
        {
            Name = "a porter’s bracelet";
            Hue = 0x489;

            if (ValierCarryCapacityConfig.Blessed)
                LootType = LootType.Blessed;
        }

        public ValierCarryBracelet(Serial serial) : base(serial) { }

        public int CarryCapacityBonus
        {
            get { return ValierCarryCapacityConfig.BraceletBonus; }
        }

        public override void GetProperties(ObjectPropertyList list)
        {
            base.GetProperties(list);

            if (ValierCarryCapacityConfig.Enabled)
                list.Add(1060658, "Carry Capacity\t+{0}", CarryCapacityBonus);
        }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }

    public class ValierCarryTalisman : GoldNecklace, ICarryCapacityBonus
    {
        [Constructable]
        public ValierCarryTalisman()
        {
            Name = "a porter’s talisman";
            Hue = 0x489;

            if (ValierCarryCapacityConfig.Blessed)
                LootType = LootType.Blessed;
        }

        public ValierCarryTalisman(Serial serial) : base(serial) { }

        public int CarryCapacityBonus
        {
            get { return ValierCarryCapacityConfig.TalismanBonus; }
        }

        public override void GetProperties(ObjectPropertyList list)
        {
            base.GetProperties(list);

            if (ValierCarryCapacityConfig.Enabled)
                list.Add(1060658, "Carry Capacity\t+{0}", CarryCapacityBonus);
        }

        public override void Serialize(GenericWriter writer) { base.Serialize(writer); writer.Write(0); }
        public override void Deserialize(GenericReader reader) { base.Deserialize(reader); reader.ReadInt(); }
    }

    // =========================================================
    // Compatibility aliases for ValierShop.shop type names
    // (so "Item type not found: ValierPackMuleSash/Ring" never happens)
    // These map the shop entries to the new pure-carry-capacity items.
    // =========================================================

    // Shop expects: ValierPackMuleSash
    public class ValierPackMuleSash : ValierCarrySash
    {
        [Constructable]
        public ValierPackMuleSash() : base()
        {
            Name = "Pack Mule Sash";
        }

        public ValierPackMuleSash(Serial serial) : base(serial) { }

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

    // Shop expects: ValierPackMuleRing
    public class ValierPackMuleRing : ValierCarryRing
    {
        [Constructable]
        public ValierPackMuleRing() : base()
        {
            Name = "Pack Mule Ring";
        }

        public ValierPackMuleRing(Serial serial) : base(serial) { }

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
