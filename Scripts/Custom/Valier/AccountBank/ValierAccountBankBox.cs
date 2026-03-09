// ValierAccountBankBox.cs
// ValierUO Account Bank container (shared per account)
//
// v7 FIX: Allows depositing items reliably (unlimited weight like BankBox)
// - Overrides DefaultMaxWeight = 0 (unlimited)
// - Overrides IsVirtualItem = true (bank-style behavior)
// - Overrides IsAccessibleTo / OnDragDrop / OnDragDropInto to allow only the current opener while open
// - Keeps BankBox-style open (EquipUpdate + DisplayTo)

using System;

using Server;
using Server.Items;
using Server.Mobiles;
using Server.Network;

namespace Server.Custom.Valier
{
    public class ValierAccountBankBox : Container
    {
        private string _accountName;

        private Mobile _opener;
        private bool _open;

        private Timer _autoCloseTimer;
        private int _autoCloseRange;
        private double _autoCloseCheckSeconds;

        [CommandProperty(AccessLevel.GameMaster)]
        public string AccountName
        {
            get { return _accountName; }
            set { _accountName = value; }
        }

        public override bool Decays { get { return false; } }

        // Match BankBox behavior
        public override int DefaultMaxWeight { get { return 0; } } // unlimited
        public override bool IsVirtualItem { get { return true; } }

        [Constructable]
        public ValierAccountBankBox() : this(String.Empty)
        {
        }

        public ValierAccountBankBox(string accountName) : base(0xE7C)
        {
            _accountName = accountName ?? String.Empty;

            Name = "Account Bank Box";
            Movable = false;
            Visible = true; // must be visible while open so players can CanSee() it for drag/drop
                LootType = LootType.Blessed;
        }

        public ValierAccountBankBox(Serial serial) : base(serial)
        {
        }

        private bool IsOpenFor(Mobile m)
        {
            return _open && _opener != null && m == _opener;
        }

        public bool OpenFor(PlayerMobile opener, Layer introLayer)
        {
            if (opener == null || opener.Deleted || opener.NetState == null)
                return false;

            try
            {
                Movable = false;
                Visible = true; // must be visible while open so players can CanSee() it for drag/drop
                LootType = LootType.Blessed;

                // If attached to a different mobile/container, detach first
                if (Parent is Mobile && Parent != opener)
                {
                    ((Mobile)Parent).RemoveItem(this);
                    Internalize();
                }
                else if (Parent is Container)
                {
                    ((Container)Parent).RemoveItem(this);
                    Internalize();
                }

                Layer = introLayer;

                // Safety: if layer occupied, can't equip
                Item existing = opener.FindItemOnLayer(introLayer);
                if (existing != null && existing != this)
                {
                    opener.SendMessage(0x22, "Account bank layer is occupied. Ask staff to change IntroLayer in config.");
                    return false;
                }

                // Equip + EquipUpdate (BankBox style)
                opener.EquipItem(this);
                opener.Send(new EquipUpdate(this));

                _opener = opener;
                _open = true;

                // Open container gump
                DisplayTo(opener);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ValierAccountBank] OpenFor exception: {0}", ex);
                return false;
            }
        }

        
        public void BeginAutoCloseMonitor(PlayerMobile opener, int range, double checkSeconds)
        {
            try
            {
                _autoCloseRange = Math.Max(1, Math.Min(12, range));
                _autoCloseCheckSeconds = checkSeconds < 0.2 ? 0.2 : (checkSeconds > 2.0 ? 2.0 : checkSeconds);

                if (_autoCloseTimer != null)
                {
                    _autoCloseTimer.Stop();
                    _autoCloseTimer = null;
                }

                // Only run while opened
                _autoCloseTimer = Timer.DelayCall(TimeSpan.FromSeconds(_autoCloseCheckSeconds), TimeSpan.FromSeconds(_autoCloseCheckSeconds), delegate
                {
                    try
                    {
                        if (!_open || _opener == null || _opener.Deleted || _opener.NetState == null)
                        {
                            StopAutoCloseTimer();
                            return;
                        }

                        PlayerMobile pm = _opener as PlayerMobile;
                        if (pm == null)
                        {
                            StopAutoCloseTimer();
                            return;
                        }

                        // If system requires banker proximity and player is no longer near a banker, close.
                        if (Server.Custom.Valier.ValierAccountBankSystem.RequireNearBanker &&
                            !Server.Custom.Valier.ValierAccountBankSystem.IsNearBanker(pm, _autoCloseRange))
                        {
                            pm.SendMessage(0x22, "Your account bank closes as you move away from the banker.");
                            CloseFor(pm);
                            return;
                        }
                    }
                    catch
                    {
                        // If anything odd happens, fail safe closed
                        try
                        {
                            if (_opener is PlayerMobile)
                                CloseFor((PlayerMobile)_opener);
                        }
                        catch { }

                        StopAutoCloseTimer();
                    }
                });
            }
            catch { }
        }

        private void StopAutoCloseTimer()
        {
            try
            {
                if (_autoCloseTimer != null)
                {
                    _autoCloseTimer.Stop();
                    _autoCloseTimer = null;
                }
            }
            catch { }
        }

        private void CloseFor(PlayerMobile pm)
        {
            try
            {
                // This will remove the equipped item from the client, which also closes the container window.
                if (pm != null && pm.NetState != null)
                {
                    pm.Send(RemovePacket);
                }
            }
            catch { }

            DetachAndInternalize(pm);
            StopAutoCloseTimer();
        }

public void DetachAndInternalize(PlayerMobile who)
        {
            try
            {
                if (who != null && Parent == who)
                {
                    who.RemoveItem(this);
                }

                _open = false;
                _opener = null;


                Visible = false;
                Internalize();
                StopAutoCloseTimer();
            }
            catch { }
        }

        public override bool IsAccessibleTo(Mobile check)
        {
            if (IsOpenFor(check) || (check != null && check.AccessLevel >= AccessLevel.GameMaster))
                return base.IsAccessibleTo(check);

            return false;
        }

        public override bool OnDragDrop(Mobile from, Item dropped)
        {
            if (IsOpenFor(from) || (from != null && from.AccessLevel >= AccessLevel.GameMaster))
                return base.OnDragDrop(from, dropped);

            return false;
        }

        public override bool OnDragDropInto(Mobile from, Item item, Point3D p)
        {
            if (IsOpenFor(from) || (from != null && from.AccessLevel >= AccessLevel.GameMaster))
                return base.OnDragDropInto(from, item, p);

            return false;
        }

        public override void OnSingleClick(Mobile from)
        { }

        public override void OnDoubleClick(Mobile from)
        {
            // Prevent direct open by clicking (it's invisible anyway)
            if (from != null)
                from.SendMessage(0x22, "Use [AccountBank near a banker to access your account bank.");
        }

        public override DeathMoveResult OnParentDeath(Mobile parent)
        {
            // Don't allow it to drop
            return DeathMoveResult.RemainEquiped;
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write(3); // version
            writer.Write(_accountName);

            // do NOT serialize opener/open state
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);

            int version = reader.ReadInt();

            if (version >= 1)
                _accountName = reader.ReadString();

            Movable = false;
            Visible = true; // must be visible while open so players can CanSee() it for drag/drop
                LootType = LootType.Blessed;

            _open = false;
            _opener = null;


                Visible = false;
            // Keep it internal until opened
            try { Internalize(); } catch { }
        }
    }
}
