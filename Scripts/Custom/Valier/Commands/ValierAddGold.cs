// ValierAddGold.cs
// ServUO 57.x - GM command to deposit gold into a targeted player's NORMAL BankBox
//
// Usage:
//   [ValierAddGold <amount>
//   -> target a player; gold is deposited into their BankBox (same one used by bankers / [withdraw).
//
// Install:
//   Scripts/Custom/Valier/Commands/ValierAddGold.cs  (file name can be anything)
//
// Notes:
// - Uses CommandEventArgs.Arguments parsing (ServUO-safe) instead of GetInt64.
// - Splits into safe pile sizes to avoid stack limits.

using System;

using Server;
using Server.Commands;
using Server.Items;
using Server.Mobiles;
using Server.Targeting;

namespace Server.Custom.Valier
{
    public static class ValierAddGoldCommand
    {
        // Safe default stack size for most ServUO shards
        private const int MaxGoldPerPile = 60000;

        public static void Initialize()
        {
            CommandSystem.Register("ValierAddGold", AccessLevel.GameMaster, OnCommand);
        }

        [Usage("ValierAddGold <amount>")]
        [Description("Deposits gold into the targeted player's normal BankBox.")]
        private static void OnCommand(CommandEventArgs e)
        {
            Mobile from = e.Mobile;

            if (from == null)
                return;

            if (e == null || e.Length < 1)
            {
                from.SendMessage(0x22, "Usage: [ValierAddGold <amount>  (then target a player)");
                return;
            }

            int amount;

            // ServUO CommandEventArgs typically provides Arguments[]
            // We parse manually for maximum compatibility.
            if (e.Arguments == null || e.Arguments.Length < 1 || !Int32.TryParse(e.Arguments[0], out amount))
            {
                from.SendMessage(0x22, "Invalid amount.");
                return;
            }

            if (amount <= 0)
            {
                from.SendMessage(0x22, "Amount must be > 0.");
                return;
            }

            from.SendMessage(0x59, "Target a player to deposit {0:N0} gold into their bank box.", amount);
            from.Target = new AddGoldTarget(amount);
        }

        private sealed class AddGoldTarget : Target
        {
            private readonly int _amount;

            public AddGoldTarget(int amount)
                : base(12, false, TargetFlags.None)
            {
                _amount = amount;
            }

            protected override void OnTarget(Mobile from, object targeted)
            {
                PlayerMobile pm = targeted as PlayerMobile;

                if (pm == null || pm.Deleted)
                {
                    from.SendMessage(0x22, "That is not a player.");
                    return;
                }

                BankBox box = pm.BankBox; // creates if missing

                if (box == null || box.Deleted)
                {
                    from.SendMessage(0x22, "Could not access that player's bank box.");
                    return;
                }

                int remaining = _amount;
                int deposited = 0;

                while (remaining > 0)
                {
                    int pile = remaining > MaxGoldPerPile ? MaxGoldPerPile : remaining;

                    Gold gold = new Gold(pile);

                    try
                    {
                        box.DropItem(gold);
                        deposited += pile;
                        remaining -= pile;
                    }
                    catch
                    {
                        gold.Delete();
                        break;
                    }
                }

                if (deposited <= 0)
                {
                    from.SendMessage(0x22, "Deposit failed (could not place gold into the bank box).");
                    return;
                }

                if (remaining > 0)
                {
                    from.SendMessage(0x22, "Deposited {0:N0} gold, but {1:N0} could not be deposited.", deposited, remaining);
                    pm.SendMessage(0x22, "A GM attempted to deposit gold into your bank, but some could not be deposited.");
                }
                else
                {
                    from.SendMessage(0x59, "Deposited {0:N0} gold into {1}'s bank box.", deposited, pm.Name);
                    pm.SendMessage(0x59, "{0:N0} gold has been deposited into your bank box.", deposited);
                }
            }
        }
    }
}
