// ValierAccountBankContextMenu.cs
// Adds a banker context menu entry for Account Bank (no core edits).
//
// Note: The text uses an existing cliloc (6145 = "Open Backpack") because custom labels require custom clilocs.

using System;

using Server;
using Server.Accounting;
using Server.ContextMenus;
using Server.Mobiles;

namespace Server.Custom.Valier
{
    public static class ValierAccountBankContextMenu
    {
        public static void Initialize()
        {
            EventSink.ContextMenu += OnContextMenu;
        }

        private static void OnContextMenu(ContextMenuEventArgs e)
        {
            if (e == null || e.Entries == null)
                return;

            if (!ValierAccountBankSystem.Enabled)
                return;

            PlayerMobile pm = e.Mobile as PlayerMobile;
            if (pm == null || pm.Deleted || !pm.Alive)
                return;

            Mobile target = e.Target as Mobile;
            if (target == null || target.Deleted)
                return;

            if (!IsBankerTarget(target))
                return;

            for (int i = 0; i < e.Entries.Count; i++)
            {
                if (e.Entries[i] is OpenAccountBankEntry)
                    return;
            }

            var entry = new OpenAccountBankEntry(target);

            entry.Enabled = !pm.Criminal;

            Account acc = pm.Account as Account;
            if (acc != null && ValierAccountBankSystem.RequireUnlockScroll && !ValierAccountBankSystem.UnlockedByDefault)
            {
                if (!ValierAccountBankSystem.IsUnlocked(acc))
                    entry.Enabled = false;
            }

            e.Entries.Add(entry);
        }

        private static bool IsBankerTarget(Mobile m)
        {
            if (m is Banker)
                return true;

            BaseVendor v = m as BaseVendor;

            if (v != null)
            {
                string title = v.Title ?? String.Empty;
                string name = v.Name ?? String.Empty;

                if (title.IndexOf("bank", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (name.IndexOf("bank", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private class OpenAccountBankEntry : ContextMenuEntry
        {
            private readonly Mobile _banker;

            public OpenAccountBankEntry(Mobile banker) : base(6145, 12)
            {
                _banker = banker;
            }

            public override void OnClick()
            {
                if (!Owner.From.CheckAlive())
                    return;

                if (Owner.From.Criminal)
                {
                    if (_banker != null)
                        _banker.Say(500378);
                    return;
                }

                PlayerMobile pm = Owner.From as PlayerMobile;
                if (pm == null)
                    return;

                ValierAccountBankSystem.OpenFor(pm);
            }
        }
    }
}
