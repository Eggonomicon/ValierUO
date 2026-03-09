// ValierMarketCompat.cs
// Stage 0 compatibility layer for older ServUO branches
// Purpose:
// - Provide missing legacy helper used by generated market scripts
// - Provide WeaponQuality / ArmorQuality aliases that map to ItemQuality
// - Let us modernize in stages without forcing immediate core rewrites

using System;

using Server;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.Valier.Market
{
    public static class ValierMarketCompat
    {
        private static readonly Layer[] _layers = new Layer[]
        {
            Layer.OneHanded,
            Layer.TwoHanded,
            Layer.Shoes,
            Layer.Pants,
            Layer.Shirt,
            Layer.Helm,
            Layer.Gloves,
            Layer.Ring,
            Layer.Neck,
            Layer.Hair,
            Layer.Waist,
            Layer.InnerTorso,
            Layer.Bracelet,
            Layer.MiddleTorso,
            Layer.Earrings,
            Layer.Arms,
            Layer.Cloak,
            Layer.OuterTorso,
            Layer.OuterLegs,
            Layer.InnerLegs,
            Layer.Talisman,
            Layer.Face,
            Layer.Backpack // safe for NPC outfit reset; skipped if container/null
        };

        public static void ClearLayers(Mobile m)
        {
            if (m == null)
                return;

            for (int i = 0; i < _layers.Length; i++)
            {
                Item item = m.FindItemOnLayer(_layers[i]);

                if (item == null)
                    continue;

                // Never delete the backpack/container on the NPC
                if (_layers[i] == Layer.Backpack)
                    continue;

                try
                {
                    item.Delete();
                }
                catch
                {
                }
            }
        }
    }

    // Compatibility aliases:
    // Older generated scripts referenced WeaponQuality / ArmorQuality.
    // Your shard uses ItemQuality on both BaseWeapon and BaseArmor.
    public static class WeaponQuality
    {
        public const ItemQuality Low = ItemQuality.Low;
        public const ItemQuality Normal = ItemQuality.Normal;
        public const ItemQuality Regular = ItemQuality.Normal;
        public const ItemQuality Exceptional = ItemQuality.Exceptional;
    }

    public static class ArmorQuality
    {
        public const ItemQuality Low = ItemQuality.Low;
        public const ItemQuality Normal = ItemQuality.Normal;
        public const ItemQuality Regular = ItemQuality.Normal;
        public const ItemQuality Exceptional = ItemQuality.Exceptional;
    }
}
