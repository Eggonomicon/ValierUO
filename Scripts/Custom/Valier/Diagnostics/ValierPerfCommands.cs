using System;
using System.Diagnostics;

using Server;
using Server.Commands;
using Server.Configuration;

namespace Server.Custom.Valier
{
    public class ValierPerfCommands
    {
        public static void Initialize()
        {
            // Always register; you can disable visibility via config if desired.
            CommandSystem.Register("ValierPerf", AccessLevel.GameMaster, OnPerf);
            CommandSystem.Register("ValierUOConfigReload", AccessLevel.Administrator, OnReloadConfig);
            CommandSystem.Register("ValierUOInfo", AccessLevel.GameMaster, OnInfo);
        }

        private static bool CommandsEnabled()
        {
            ValierUOConfig.EnsureLoaded();
            return ValierUOConfig.EnablePerfCommands;
        }

        private static void OnInfo(CommandEventArgs e)
        {
            if (!CommandsEnabled())
            {
                e.Mobile.SendMessage(0x22, "ValierUO perf commands are disabled (ValierUO.EnablePerfCommands=false). ");
                return;
            }

            ValierUOConfig.EnsureLoaded();

            e.Mobile.SendMessage(0x59, "{0} - config: SaveStrategy={1}, PermitBackgroundWriteDefault={2}",
                ValierUOConfig.BrandName, ValierUOConfig.SaveStrategy, ValierUOConfig.PermitBackgroundWriteDefault);
        }

        private static void OnReloadConfig(CommandEventArgs e)
        {
            ValierUOConfig.ReloadAllConfigs();

            e.Mobile.SendMessage(0x59, "Reloaded all Config/*.cfg. ValierUO settings: SaveStrategy={0}, PermitBackgroundWriteDefault={1}",
                ValierUOConfig.SaveStrategy, ValierUOConfig.PermitBackgroundWriteDefault);
        }

        private static void OnPerf(CommandEventArgs e)
        {
            if (!CommandsEnabled())
            {
                e.Mobile.SendMessage(0x22, "ValierUO perf commands are disabled (ValierUO.EnablePerfCommands=false). ");
                return;
            }

            var m = e.Mobile;

            ValierUOConfig.EnsureLoaded();

            TimeSpan up = DateTime.UtcNow - Core.Process.StartTime.ToUniversalTime();

            long mem = GC.GetTotalMemory(false);

            string lastSave = World.LastSaveUtc == DateTime.MinValue
                ? "(none yet)"
                : String.Format("{0} UTC, {1:F2}s, strategy={2}", World.LastSaveUtc.ToString("yyyy-MM-dd HH:mm:ss"), World.LastSaveDuration.TotalSeconds, World.LastSaveStrategyName ?? "?");

            m.SendMessage(0x59, "{0} Perf Snapshot", ValierUOConfig.BrandName);
            m.SendMessage(0x59, "Uptime: {0}d {1}h {2}m", (int)up.TotalDays, up.Hours, up.Minutes);
            m.SendMessage(0x59, "Online: {0}   Items: {1}   Mobiles: {2}", NetState.Instances.Count, World.Items.Count, World.Mobiles.Count);
            m.SendMessage(0x59, "Memory (managed): {0:N0} MB", mem / 1024.0 / 1024.0);
            m.SendMessage(0x59, "Last save: {0}", lastSave);
            m.SendMessage(0x59, "Config: SaveStrategy={0} PermitBackgroundWriteDefault={1}", ValierUOConfig.SaveStrategy, ValierUOConfig.PermitBackgroundWriteDefault);

            if (Core.Profiling)
            {
                m.SendMessage(0x59, "Core profiling: ENABLED (ProfileTime={0})", Core.ProfileTime);
            }
            else
            {
                m.SendMessage(0x59, "Core profiling: disabled (use [SetProfiles true to enable)");
            }

            // Optional: show process working set on Windows
            try
            {
                using (var proc = Process.GetCurrentProcess())
                {
                    m.SendMessage(0x59, "Working Set: {0:N0} MB", proc.WorkingSet64 / 1024.0 / 1024.0);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
