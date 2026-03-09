// ValierMarketSystem.cs
// ValierUO: Market Broker (Resources + Scrolls)
//
// Uses .shop-style text configs (same idea as ValierShop.shop):
//   Config/ValierBuyOrders.shop
//   Config/ValierScrollMarket.shop
//
// Features
// - Resource Buy Orders: turn in items from Backpack OR Turn-In Crate
// - Scroll Market: purchase PowerScrolls with Gold and/or ValierCoins
// - Daily limits per account (Account tags) + optional daily coin cap
// - Rotation state persists in Saves/ValierMarket/ValierMarket.state
//
// GM
// - [add ValierMarketBroker
// - [add ValierTurnInCrate
// - [ValierMarketReload
// - [ValierMarketRotate
//
// Player
// - Double-click the broker (or say "market")
// - Or use: [ValierMarket
//
// NOTE: Uses reflection to integrate with your wallet (ValierWalletSystem) so it
// won’t care what namespace you used.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

using Server;
using Server.Accounting;
using Server.Commands;
using Server.Items;
using Server.Mobiles;

namespace Server.Custom.ValierMarket
{
	public static class ValierMarketSystem
	{
		private static readonly object _sync = new object();

		public static string BuyOrdersCfgPath { get { return Path.Combine(Core.BaseDirectory, "Config", "ValierBuyOrders.shop"); } }
		public static string ScrollMarketCfgPath { get { return Path.Combine(Core.BaseDirectory, "Config", "ValierScrollMarket.shop"); } }

		private static string StateDir { get { return Path.Combine(Core.BaseDirectory, "Saves", "ValierMarket"); } }
		private static string StatePath { get { return Path.Combine(StateDir, "ValierMarket.state"); } }

		private static readonly List<BuyOrderDef> _buyOrderPool = new List<BuyOrderDef>();
		private static readonly List<ScrollListingDef> _scrollPool = new List<ScrollListingDef>();

		private static string _activeDateKey = ""; // yyyyMMdd (UTC)
		private static readonly List<string> _activeBuyOrderIds = new List<string>();
		private static readonly List<string> _activeScrollIds = new List<string>();

		public static bool Enabled = true;

		public static bool RotateBuyOrdersDaily = true;
		public static int ActiveBuyOrders = 4;

		public static bool RotateScrollsDaily = false;
		public static int ActiveScrolls = 12;

		public static int DailyCoinCapPerAccount = 200; // coins/day from buy orders
		public static int InteractionRange = 3;

		public static void Initialize()
		{
			LoadAll();

			CommandSystem.Register("ValierMarket", AccessLevel.Player, e =>
			{
				PlayerMobile pm = e.Mobile as PlayerMobile;
				if (pm != null)
					OpenGump(pm, 0);
			});

			CommandSystem.Register("ValierMarketReload", AccessLevel.GameMaster, e =>
			{
				LoadAll();
				e.Mobile.SendMessage(0x59, "ValierMarket: configs reloaded.");
			});

			CommandSystem.Register("ValierMarketRotate", AccessLevel.GameMaster, e =>
			{
				Rotate(true);
				e.Mobile.SendMessage(0x59, "ValierMarket: rotation forced.");
			});

			CommandSystem.Register("ValierMarketInfo", AccessLevel.GameMaster, e =>
			{
				lock (_sync)
				{
					e.Mobile.SendMessage(0x59, "ValierMarket Enabled={0} Date={1}", Enabled, _activeDateKey);
					e.Mobile.SendMessage(0x59, "BuyOrders pool={0} active={1} rotateDaily={2}", _buyOrderPool.Count, _activeBuyOrderIds.Count, RotateBuyOrdersDaily);
					e.Mobile.SendMessage(0x59, "Scrolls pool={0} active={1} rotateDaily={2}", _scrollPool.Count, _activeScrollIds.Count, RotateScrollsDaily);
					e.Mobile.SendMessage(0x59, "DailyCoinCapPerAccount={0} InteractionRange={1}", DailyCoinCapPerAccount, InteractionRange);
				}
			});

			Console.WriteLine("[ValierMarket] Initialized. BuyOrdersCfg={0} ScrollCfg={1}", BuyOrdersCfgPath, ScrollMarketCfgPath);
		}

		public static void OpenGump(PlayerMobile pm, int tab)
		{
			if (pm == null || pm.Deleted)
				return;

			if (!Enabled)
			{
				pm.SendMessage(0x22, "The market broker is currently disabled.");
				return;
			}

			Rotate(false);

			pm.CloseGump(typeof(ValierMarketGump));
			pm.SendGump(new ValierMarketGump(pm, tab));
		}

		public static List<BuyOrderDef> GetActiveBuyOrders()
		{
			lock (_sync)
			{
				Rotate(false);

				List<BuyOrderDef> list = new List<BuyOrderDef>();

				for (int i = 0; i < _activeBuyOrderIds.Count; i++)
				{
					BuyOrderDef d = _buyOrderPool.Find(x => String.Equals(x.Id, _activeBuyOrderIds[i], StringComparison.OrdinalIgnoreCase));
					if (d != null)
						list.Add(d);
				}

				return list;
			}
		}

		public static List<ScrollListingDef> GetActiveScrolls()
		{
			lock (_sync)
			{
				Rotate(false);

				List<ScrollListingDef> list = new List<ScrollListingDef>();

				for (int i = 0; i < _activeScrollIds.Count; i++)
				{
					ScrollListingDef d = _scrollPool.Find(x => String.Equals(x.Id, _activeScrollIds[i], StringComparison.OrdinalIgnoreCase));
					if (d != null)
						list.Add(d);
				}

				return list;
			}
		}

		private static string TodayKeyUtc()
		{
			return DateTime.UtcNow.ToString("yyyyMMdd");
		}

		private static void EnsureStateDir()
		{
			try
			{
				if (!Directory.Exists(StateDir))
					Directory.CreateDirectory(StateDir);
			}
			catch { }
		}

		private static void LoadAll()
		{
			lock (_sync)
			{
				LoadBuyOrdersCfg();
				LoadScrollMarketCfg();
				LoadState();
				Rotate(false);
			}
		}

		private static void LoadState()
		{
			_activeDateKey = "";
			_activeBuyOrderIds.Clear();
			_activeScrollIds.Clear();

			try
			{
				if (!File.Exists(StatePath))
					return;

				string[] lines = File.ReadAllLines(StatePath);

				for (int i = 0; i < lines.Length; i++)
				{
					string line = lines[i].Trim();
					if (line.Length == 0 || line.StartsWith("#"))
						continue;

					int eq = line.IndexOf('=');
					if (eq <= 0)
						continue;

					string key = line.Substring(0, eq).Trim();
					string val = line.Substring(eq + 1).Trim();

					if (String.Equals(key, "Date", StringComparison.OrdinalIgnoreCase))
						_activeDateKey = val;
					else if (String.Equals(key, "BuyOrders", StringComparison.OrdinalIgnoreCase))
						_activeBuyOrderIds.AddRange(SplitCsv(val));
					else if (String.Equals(key, "Scrolls", StringComparison.OrdinalIgnoreCase))
						_activeScrollIds.AddRange(SplitCsv(val));
				}
			}
			catch { }
		}

		private static void SaveState()
		{
			try
			{
				EnsureStateDir();

				using (StreamWriter w = new StreamWriter(StatePath, false))
				{
					w.WriteLine("# ValierMarket rotation state");
					w.WriteLine("Date={0}", _activeDateKey);
					w.WriteLine("BuyOrders={0}", String.Join(",", _activeBuyOrderIds.ToArray()));
					w.WriteLine("Scrolls={0}", String.Join(",", _activeScrollIds.ToArray()));
				}
			}
			catch { }
		}

		private static List<string> SplitCsv(string s)
		{
			List<string> list = new List<string>();

			if (String.IsNullOrWhiteSpace(s))
				return list;

			string[] parts = s.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			for (int i = 0; i < parts.Length; i++)
			{
				string v = parts[i].Trim();
				if (v.Length > 0)
					list.Add(v);
			}

			return list;
		}

		private static void Rotate(bool force)
		{
			if (!Enabled)
				return;

			string today = TodayKeyUtc();
			bool dateChanged = !String.Equals(today, _activeDateKey, StringComparison.OrdinalIgnoreCase);

			if (!force && !dateChanged)
				return;

			_activeDateKey = today;

			if (RotateBuyOrdersDaily || force || _activeBuyOrderIds.Count == 0)
			{
				_activeBuyOrderIds.Clear();
				_activeBuyOrderIds.AddRange(PickRandomIds(GetBuyOrderIds(), ActiveBuyOrders));
			}

			if (RotateScrollsDaily || force || _activeScrollIds.Count == 0)
			{
				_activeScrollIds.Clear();
				_activeScrollIds.AddRange(PickRandomIds(GetScrollIds(), ActiveScrolls));
			}

			SaveState();
		}

		private static List<string> GetBuyOrderIds()
		{
			List<string> ids = new List<string>();
			for (int i = 0; i < _buyOrderPool.Count; i++)
				ids.Add(_buyOrderPool[i].Id);
			return ids;
		}

		private static List<string> GetScrollIds()
		{
			List<string> ids = new List<string>();
			for (int i = 0; i < _scrollPool.Count; i++)
				ids.Add(_scrollPool[i].Id);
			return ids;
		}

		private static List<string> PickRandomIds(List<string> ids, int take)
		{
			List<string> result = new List<string>();

			if (ids == null || ids.Count == 0 || take <= 0)
				return result;

			for (int i = ids.Count - 1; i > 0; i--)
			{
				int j = Utility.Random(i + 1);
				string tmp = ids[i];
				ids[i] = ids[j];
				ids[j] = tmp;
			}

			int n = Math.Min(take, ids.Count);
			for (int i = 0; i < n; i++)
				result.Add(ids[i]);

			return result;
		}

		// -------------------------
		// Config parsing
		// -------------------------

		private static List<string> SafeReadLines(string path)
		{
			List<string> list = new List<string>();

			try
			{
				if (File.Exists(path))
					list.AddRange(File.ReadAllLines(path));
			}
			catch { }

			return list;
		}

		private static string StripComment(string line)
		{
			if (line == null)
				return "";

			int h = line.IndexOf('#');
			return h >= 0 ? line.Substring(0, h) : line;
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

		private static int Clamp(int v, int min, int max)
		{
			if (v < min) return min;
			if (v > max) return max;
			return v;
		}

		private static string Safe(string[] p, int idx)
		{
			if (p == null || idx < 0 || idx >= p.Length)
				return "";

			return (p[idx] ?? "").Trim();
		}

		private static void LoadBuyOrdersCfg()
		{
			_buyOrderPool.Clear();

			// defaults
			Enabled = true;
			RotateBuyOrdersDaily = true;
			ActiveBuyOrders = 4;
			DailyCoinCapPerAccount = 200;
			InteractionRange = 3;

			List<string> lines = SafeReadLines(BuyOrdersCfgPath);

			for (int i = 0; i < lines.Count; i++)
			{
				string line = StripComment(lines[i]).Trim();
				if (line.Length == 0)
					continue;

				int eq = line.IndexOf('=');

				if (eq > 0 && line.IndexOf('|') < 0)
				{
					string k = line.Substring(0, eq).Trim();
					string v = line.Substring(eq + 1).Trim();

					if (String.Equals(k, "Enabled", StringComparison.OrdinalIgnoreCase))
						Enabled = ToBool(v, true);
					else if (String.Equals(k, "RotateBuyOrdersDaily", StringComparison.OrdinalIgnoreCase))
						RotateBuyOrdersDaily = ToBool(v, true);
					else if (String.Equals(k, "ActiveBuyOrders", StringComparison.OrdinalIgnoreCase))
						ActiveBuyOrders = Clamp(ToInt(v, 4), 0, 20);
					else if (String.Equals(k, "DailyCoinCapPerAccount", StringComparison.OrdinalIgnoreCase))
						DailyCoinCapPerAccount = Clamp(ToInt(v, 200), 0, 1000000);
					else if (String.Equals(k, "InteractionRange", StringComparison.OrdinalIgnoreCase))
						InteractionRange = Clamp(ToInt(v, 3), 1, 12);

					continue;
				}

				if (line.StartsWith("order|", StringComparison.OrdinalIgnoreCase))
				{
					string[] p = line.Split('|');
					if (p.Length < 8)
						continue;

					BuyOrderDef def = new BuyOrderDef();
					def.Id = Safe(p, 1);
					def.DisplayName = Safe(p, 2);
					def.TypeName = Safe(p, 3);
					def.AmountPerTurnIn = Clamp(ToInt(Safe(p, 4), 100), 1, 10000000);
					def.CoinsReward = Clamp(ToInt(Safe(p, 5), 0), 0, 1000000);
					def.GoldReward = Clamp(ToInt(Safe(p, 6), 0), 0, 2000000000);
					def.MaxTurnInsPerDay = Clamp(ToInt(Safe(p, 7), 5), 0, 1000000);
					def.Notes = p.Length >= 9 ? Safe(p, 8) : "";

					if (!String.IsNullOrWhiteSpace(def.Id))
						_buyOrderPool.Add(def);
				}
			}
		}

		private static void LoadScrollMarketCfg()
		{
			_scrollPool.Clear();

			RotateScrollsDaily = false;
			ActiveScrolls = 12;

			List<string> lines = SafeReadLines(ScrollMarketCfgPath);

			for (int i = 0; i < lines.Count; i++)
			{
				string line = StripComment(lines[i]).Trim();
				if (line.Length == 0)
					continue;

				int eq = line.IndexOf('=');

				if (eq > 0 && line.IndexOf('|') < 0)
				{
					string k = line.Substring(0, eq).Trim();
					string v = line.Substring(eq + 1).Trim();

					if (String.Equals(k, "RotateScrollsDaily", StringComparison.OrdinalIgnoreCase))
						RotateScrollsDaily = ToBool(v, false);
					else if (String.Equals(k, "ActiveScrolls", StringComparison.OrdinalIgnoreCase))
						ActiveScrolls = Clamp(ToInt(v, 12), 0, 60);

					continue;
				}

				if (line.StartsWith("scroll|", StringComparison.OrdinalIgnoreCase))
				{
					string[] p = line.Split('|');
					if (p.Length < 9)
						continue;

					ScrollListingDef def = new ScrollListingDef();
					def.Id = Safe(p, 1);
					def.DisplayName = Safe(p, 2);
					def.SkillNameText = Safe(p, 3);
					def.Value = ToDouble(Safe(p, 4), 105.0);
					def.PriceGold = Clamp(ToInt(Safe(p, 5), 0), 0, 2000000000);
					def.PriceCoins = Clamp(ToInt(Safe(p, 6), 0), 0, 1000000);
					def.LimitPerDay = Clamp(ToInt(Safe(p, 7), 1), 0, 1000000);
					def.Notes = Safe(p, 8);

					if (!String.IsNullOrWhiteSpace(def.Id))
						_scrollPool.Add(def);
				}
			}
		}

		// -------------------------
		// Daily account tags (date-scoped keys)
		// -------------------------

		private static Account GetAccount(PlayerMobile pm) { return pm != null ? pm.Account as Account : null; }
		private static string Prefix(PlayerMobile pm) { return "VMarket." + pm.Serial.Value.ToString(CultureInfo.InvariantCulture) + "."; }

		private static string CoinsEarnedTag(PlayerMobile pm, string dateKey) { return Prefix(pm) + "CoinsEarned." + dateKey; }
		private static string OrderCountTag(PlayerMobile pm, string orderId, string dateKey) { return Prefix(pm) + "Order." + orderId + ".Count." + dateKey; }
		private static string ScrollCountTag(PlayerMobile pm, string scrollId, string dateKey) { return Prefix(pm) + "Scroll." + scrollId + ".Count." + dateKey; }

		public static int GetCoinsEarnedToday(PlayerMobile pm)
		{
			Account acc = GetAccount(pm);
			if (acc == null)
				return 0;

			string today = TodayKeyUtc();
			int v;

			if (Int32.TryParse(acc.GetTag(CoinsEarnedTag(pm, today)), out v))
				return Math.Max(0, v);

			return 0;
		}

		private static void AddCoinsEarnedToday(PlayerMobile pm, int delta)
		{
			if (delta <= 0)
				return;

			Account acc = GetAccount(pm);
			if (acc == null)
				return;

			string today = TodayKeyUtc();
			int cur = GetCoinsEarnedToday(pm);

			acc.SetTag(CoinsEarnedTag(pm, today), (cur + delta).ToString(CultureInfo.InvariantCulture));
		}

		public static int GetOrderTurnInsToday(PlayerMobile pm, string orderId)
		{
			Account acc = GetAccount(pm);
			if (acc == null)
				return 0;

			string today = TodayKeyUtc();
			int v;

			if (Int32.TryParse(acc.GetTag(OrderCountTag(pm, orderId, today)), out v))
				return Math.Max(0, v);

			return 0;
		}

		private static void AddOrderTurnInsToday(PlayerMobile pm, string orderId, int delta)
		{
			if (delta <= 0)
				return;

			Account acc = GetAccount(pm);
			if (acc == null)
				return;

			string today = TodayKeyUtc();
			int cur = GetOrderTurnInsToday(pm, orderId);

			acc.SetTag(OrderCountTag(pm, orderId, today), (cur + delta).ToString(CultureInfo.InvariantCulture));
		}

		public static int GetScrollBuysToday(PlayerMobile pm, string scrollId)
		{
			Account acc = GetAccount(pm);
			if (acc == null)
				return 0;

			string today = TodayKeyUtc();
			int v;

			if (Int32.TryParse(acc.GetTag(ScrollCountTag(pm, scrollId, today)), out v))
				return Math.Max(0, v);

			return 0;
		}

		private static void AddScrollBuysToday(PlayerMobile pm, string scrollId, int delta)
		{
			if (delta <= 0)
				return;

			Account acc = GetAccount(pm);
			if (acc == null)
				return;

			string today = TodayKeyUtc();
			int cur = GetScrollBuysToday(pm, scrollId);

			acc.SetTag(ScrollCountTag(pm, scrollId, today), (cur + delta).ToString(CultureInfo.InvariantCulture));
		}

		// -------------------------
		// Broker proximity
		// -------------------------

		public static bool IsNearBroker(PlayerMobile pm)
		{
			if (pm == null || pm.Map == null)
				return false;

			try
			{
				foreach (Mobile m in pm.GetMobilesInRange(InteractionRange))
				{
					if (m is ValierMarketBroker)
						return true;
				}
			}
			catch { }

			return false;
		}

		// -------------------------
		// Buy order turn-ins
		// -------------------------

		public static TurnInResult TryTurnIn(PlayerMobile pm, BuyOrderDef order, Container source)
		{
			if (pm == null || order == null || source == null)
				return new TurnInResult(false, "Invalid turn-in.");

			if (!Enabled)
				return new TurnInResult(false, "The market broker is disabled.");

			Rotate(false);

			if (!IsNearBroker(pm))
				return new TurnInResult(false, "You must be near a Market Broker to turn in orders.");

			int already = GetOrderTurnInsToday(pm, order.Id);
			int remainingTurnIns = Math.Max(0, order.MaxTurnInsPerDay - already);

			if (remainingTurnIns <= 0)
				return new TurnInResult(false, "You have reached the daily limit for that order.");

			int coinsEarned = GetCoinsEarnedToday(pm);
			int coinsRemaining = Math.Max(0, DailyCoinCapPerAccount - coinsEarned);

			if (order.CoinsReward > 0 && coinsRemaining <= 0)
				return new TurnInResult(false, "You have reached the daily coin cap from buy orders.");

			Type t = ResolveType(order.TypeName);
			if (t == null)
				return new TurnInResult(false, "Order type not found: " + order.TypeName);

			int available = CountAmount(source, t);

			if (available < order.AmountPerTurnIn)
				return new TurnInResult(false, String.Format("Not enough items. Need {0}. You have {1}.", order.AmountPerTurnIn, available));

			int batches = available / order.AmountPerTurnIn;

			if (batches > remainingTurnIns)
				batches = remainingTurnIns;

			if (order.CoinsReward > 0)
			{
				int capBatches = coinsRemaining / order.CoinsReward;
				if (capBatches <= 0)
					return new TurnInResult(false, "You have reached the daily coin cap from buy orders.");

				if (batches > capBatches)
					batches = capBatches;
			}

			if (batches <= 0)
				return new TurnInResult(false, "Nothing to turn in.");

			int need = order.AmountPerTurnIn * batches;
			int removed = ConsumeAmount(source, t, need);

			if (removed < need)
				return new TurnInResult(false, "Turn-in failed (not enough items after filtering).");

			int payCoins = order.CoinsReward * batches;
			int payGold = order.GoldReward * batches;

			if (payCoins > 0)
			{
				if (!TryAddValierCoins(pm, payCoins))
					return new TurnInResult(false, "Could not add Valier coins (wallet system missing?).");

				AddCoinsEarnedToday(pm, payCoins);
			}

			if (payGold > 0)
				GiveGold(pm, payGold);

			AddOrderTurnInsToday(pm, order.Id, batches);

			return new TurnInResult(true, String.Format("Turned in {0} batch(es) of {1}. Rewards: {2} coin(s), {3} gold.", batches, order.DisplayName, payCoins, payGold));
		}

		private static int CountAmount(Container source, Type t)
		{
			int total = 0;
			Item[] items = source.FindItemsByType(t, true);

			for (int i = 0; i < items.Length; i++)
			{
				Item it = items[i];
				if (it == null || it.Deleted)
					continue;

				if (!it.Movable || it.LootType == LootType.Blessed || it.LootType == LootType.Newbied)
					continue;

				total += it.Stackable ? it.Amount : 1;
			}

			return total;
		}

		private static int ConsumeAmount(Container source, Type t, int need)
		{
			int removed = 0;
			Item[] items = source.FindItemsByType(t, true);

			for (int i = 0; i < items.Length && removed < need; i++)
			{
				Item it = items[i];
				if (it == null || it.Deleted)
					continue;

				if (!it.Movable || it.LootType == LootType.Blessed || it.LootType == LootType.Newbied)
					continue;

				if (it.Stackable)
				{
					int take = Math.Min(it.Amount, need - removed);
					if (take <= 0)
						continue;

					it.Amount -= take;
					removed += take;

					if (it.Amount <= 0)
						it.Delete();
				}
				else
				{
					it.Delete();
					removed += 1;
				}
			}

			return removed;
		}

		// -------------------------
		// Scroll purchases
		// -------------------------

		public static string TryBuyScroll(PlayerMobile pm, ScrollListingDef listing)
		{
			if (pm == null || listing == null)
				return "Invalid purchase.";

			if (!Enabled)
				return "The market broker is disabled.";

			Rotate(false);

			if (!IsNearBroker(pm))
				return "You must be near a Market Broker to buy scrolls.";

			int bought = GetScrollBuysToday(pm, listing.Id);

			if (bought >= listing.LimitPerDay)
				return "You have reached the daily limit for that scroll.";

			if (listing.PriceGold > 0 && !ConsumeGold(pm, listing.PriceGold))
				return "You do not have enough gold.";

			if (listing.PriceCoins > 0 && !TryConsumeValierCoins(pm, listing.PriceCoins))
				return "You do not have enough Valier coins.";

			SkillName skill;
			if (!Enum.TryParse(listing.SkillNameText, true, out skill))
				return "Invalid skill name in config: " + listing.SkillNameText;

			PowerScroll scroll = new PowerScroll(skill, listing.Value);

			if (!String.IsNullOrWhiteSpace(listing.DisplayName))
				scroll.Name = listing.DisplayName;

			pm.AddToBackpack(scroll);
			AddScrollBuysToday(pm, listing.Id, 1);

			return "Purchased: " + listing.DisplayName;
		}

		private static bool ConsumeGold(Mobile m, int amount)
		{
			if (m == null || amount <= 0)
				return true;

			if (m.Backpack != null && m.Backpack.ConsumeTotal(typeof(Gold), amount))
				return true;

			return Banker.Withdraw(m, amount);
		}

		private static void GiveGold(Mobile m, int total)
		{
			if (m == null || total <= 0)
				return;

			if (total > 60000)
			{
				try
				{
					if (Banker.Deposit(m, total))
					{
						m.SendMessage(0x59, "Gold deposited into your bank box.");
						return;
					}
				}
				catch { }
			}

			int remaining = total;

			while (remaining > 0)
			{
				int stack = remaining > 60000 ? 60000 : remaining;
				remaining -= stack;
				m.AddToBackpack(new Gold(stack));
			}
		}

		// -------------------------
		// ValierCoins integration (reflection)
		// -------------------------

		private static Type FindWalletType()
		{
			string[] candidates = new string[]
			{
				"Server.Custom.ValierCurrency.ValierWalletSystem",
				"Server.Custom.Valier.ValierWalletSystem",
				"ValierWalletSystem"
			};

			for (int i = 0; i < candidates.Length; i++)
			{
				Type t = Type.GetType(candidates[i], false, true);
				if (t != null)
					return t;
			}

			try
			{
				var asms = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < asms.Length; i++)
				{
					Type[] types;
					try { types = asms[i].GetTypes(); }
					catch { continue; }

					for (int j = 0; j < types.Length; j++)
					{
						Type x = types[j];
						if (x == null)
							continue;

						if (String.Equals(x.Name, "ValierWalletSystem", StringComparison.OrdinalIgnoreCase))
							return x;
					}
				}
			}
			catch { }

			return null;
		}

		private static bool TryAddValierCoins(PlayerMobile pm, int amount)
		{
			if (pm == null || amount <= 0)
				return false;

			try
			{
				Type t = FindWalletType();
				if (t == null)
					return false;

				MethodInfo mi = t.GetMethod("Add", BindingFlags.Public | BindingFlags.Static);
				if (mi == null)
					return false;

				ParameterInfo[] ps = mi.GetParameters();

				if (ps.Length == 3 && ps[0].ParameterType == typeof(Mobile))
				{
					object[] args = new object[] { pm, Convert.ChangeType(amount, ps[1].ParameterType), 0L };
					mi.Invoke(null, args);
					pm.SendMessage(0x59, "You received {0} Valier coin(s).", amount);
					return true;
				}

				if (ps.Length == 2 && ps[0].ParameterType == typeof(Mobile))
				{
					object[] args = new object[] { pm, Convert.ChangeType(amount, ps[1].ParameterType) };
					mi.Invoke(null, args);
					pm.SendMessage(0x59, "You received {0} Valier coin(s).", amount);
					return true;
				}
			}
			catch { }

			return false;
		}

		private static bool TryConsumeValierCoins(PlayerMobile pm, int amount)
		{
			if (pm == null || amount <= 0)
				return true;

			try
			{
				Type t = FindWalletType();
				if (t == null)
					return false;

				MethodInfo mi = t.GetMethod("TryConsume", BindingFlags.Public | BindingFlags.Static);
				if (mi != null)
				{
					ParameterInfo[] ps = mi.GetParameters();
					if (ps.Length == 3 && ps[0].ParameterType == typeof(Mobile))
					{
						object[] args = new object[] { pm, Convert.ChangeType(amount, ps[1].ParameterType), 0L };
						object ret = mi.Invoke(null, args);
						if (ret is bool) return (bool)ret;
						return false;
					}
				}

				mi = t.GetMethod("Consume", BindingFlags.Public | BindingFlags.Static);
				if (mi != null)
				{
					ParameterInfo[] ps = mi.GetParameters();
					if (ps.Length == 2 && ps[0].ParameterType == typeof(Mobile))
					{
						object[] args = new object[] { pm, Convert.ChangeType(amount, ps[1].ParameterType) };
						object ret = mi.Invoke(null, args);
						if (ret is bool) return (bool)ret;
						return false;
					}
				}
			}
			catch { }

			return false;
		}

		// -------------------------
		// Type resolution for buy orders
		// -------------------------

		public static Type ResolveType(string typeName)
		{
			if (String.IsNullOrWhiteSpace(typeName))
				return null;

			string name = typeName.Trim();

			Type t = Type.GetType(name, false, true);
			if (t != null) return t;

			t = Type.GetType("Server.Items." + name, false, true);
			if (t != null) return t;

			try
			{
				var asms = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < asms.Length; i++)
				{
					Type[] types;
					try { types = asms[i].GetTypes(); }
					catch { continue; }

					for (int j = 0; j < types.Length; j++)
					{
						Type x = types[j];
						if (x == null) continue;

						if (String.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) ||
							(x.FullName != null && String.Equals(x.FullName, name, StringComparison.OrdinalIgnoreCase)))
							return x;
					}
				}
			}
			catch { }

			return null;
		}
	}

	public class BuyOrderDef
	{
		public string Id;
		public string DisplayName;
		public string TypeName;
		public int AmountPerTurnIn;
		public int CoinsReward;
		public int GoldReward;
		public int MaxTurnInsPerDay;
		public string Notes;
	}

	public class ScrollListingDef
	{
		public string Id;
		public string DisplayName;
		public string SkillNameText;
		public double Value;
		public int PriceGold;
		public int PriceCoins;
		public int LimitPerDay;
		public string Notes;
	}

	public class TurnInResult
	{
		public bool Success;
		public string Message;

		public TurnInResult(bool success, string message)
		{
			Success = success;
			Message = message;
		}
	}
}
