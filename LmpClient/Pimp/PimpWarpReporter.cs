using HarmonyLib;
using LmpCommon.Message.Data.Warp;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP consensus warp (plan W4.3, 26 Oct 2026): what this game reports with a warp request, a vote,
    /// and every few seconds during a group warp: whether it can take part in the requested kind of warp
    /// (and why not, in KSP's own words), the highest rate it can use, and its next Kerbal Alarm Clock
    /// alarm (the earliest alarm any game reports stops the warp; Jacob, 25 Oct).
    /// </summary>
    internal static class PimpWarpReporter
    {
        private static MethodInfo _maxRails;
        private static bool _lookedKac;
        private static FieldInfo _kacInstance, _kacAlarms;
        private static Type _kacAlarmType;
        private static PropertyInfo _alarmTime;
        private static FieldInfo _alarmName;

        public static PimpWarpReport Local(bool physics)
        {
            var r = new PimpWarpReport { CanWarp = true, Reason = string.Empty };
            var tw = TimeWarp.fetch;
            if (tw == null) { r.CanWarp = false; r.Reason = "not in a game scene"; return r; }
            var rates = physics ? tw.physicsWarpRates : tw.warpRates;
            r.MaxRate = rates.Length > 0 ? rates[rates.Length - 1] : 1;

            var v = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            if (v != null && !physics)
            {
                // KSP's own on-rails check for the active vessel (private; the same one its warp keys use).
                if (_maxRails == null) _maxRails = AccessTools.Method(typeof(TimeWarp), "getMaxOnRailsRateIdx");
                if (_maxRails != null)
                {
                    var args = new object[] { rates.Length - 1, true, null };
                    var idx = (int)_maxRails.Invoke(tw, args);
                    if (idx <= 0)
                    {
                        r.CanWarp = false;
                        r.Reason = Words(args[2]?.ToString());
                        r.MaxRate = 1;
                    }
                    else r.MaxRate = rates[Math.Min(idx, rates.Length - 1)];
                }
            }
            if (r.MaxRate <= 1 && r.CanWarp) { r.CanWarp = false; r.Reason = "cannot warp here"; }

            r.NextAlarmUt = NextAlarm(out var name);
            r.AlarmName = name ?? string.Empty;
            return r;
        }

        private static string Words(string reason)
        {
            switch (reason)
            {
                case "NOT_IN_ATMOSPHERE": return "in atmosphere";
                case "NOT_UNDER_ACCELERATION": return "under acceleration";
                case "NOT_WHILE_MOVING_OVER_SURFACE": return "moving over the surface";
                case "NOT_WHILE_ABOUT_TO_CRASH": return "about to crash";
                case "NOT_WHILE_ON_A_LADDER": return "on a ladder";
                case "NOT_WHILE_THROTTLED_UP": return "engine throttled up";
                case "ORBIT_EVENT_IMMINENT": return "orbit event imminent";
                case null: case "": return "cannot warp now";
                default: return reason.ToLowerInvariant().Replace('_', ' ');
            }
        }

        /// <summary>The earliest Kerbal Alarm Clock alarm still ahead (universe time), NaN when none.</summary>
        public static double NextAlarm(out string name)
        {
            name = null;
            try
            {
                if (!_lookedKac)
                {
                    _lookedKac = true;
                    var kacType = AssemblyLoader.loadedAssemblies.SelectMany(a => { try { return a.assembly.GetTypes(); } catch { return new Type[0]; } })
                        .FirstOrDefault(t => t.FullName == "KerbalAlarmClock.KerbalAlarmClock");
                    _kacAlarmType = AssemblyLoader.loadedAssemblies.SelectMany(a => { try { return a.assembly.GetTypes(); } catch { return new Type[0]; } })
                        .FirstOrDefault(t => t.FullName == "KerbalAlarmClock.KACAlarm");
                    _kacInstance = kacType?.GetField("APIInstance", BindingFlags.Public | BindingFlags.Static);
                    _kacAlarms = kacType?.GetField("alarms", BindingFlags.Public | BindingFlags.Static);
                    _alarmTime = _kacAlarmType?.GetProperty("AlarmTimeUT");
                    _alarmName = _kacAlarmType?.GetField("Name");
                }
                if (_kacAlarms == null || _alarmTime == null) return double.NaN;
                var list = _kacAlarms.GetValue(_kacInstance?.GetValue(null)) as IList;
                if (list == null) return double.NaN;
                var now = Planetarium.GetUniversalTime();
                var best = double.NaN;
                foreach (var a in list)
                {
                    var t = Convert.ToDouble(_alarmTime.GetValue(a, null));
                    if (!(t > now + 1)) continue;
                    if (double.IsNaN(best) || t < best) { best = t; name = _alarmName?.GetValue(a) as string; }
                }
                return best;
            }
            catch (Exception e)
            {
                LunaLog.LogWarning("[LMP][PiMP] Could not read Kerbal Alarm Clock alarms: " + e.Message);
                _kacAlarms = null;
                return double.NaN;
            }
        }
    }
}
