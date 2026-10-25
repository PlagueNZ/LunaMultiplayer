using HarmonyLib;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using System.Diagnostics;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP consensus warp (plan W4.3, 26 Oct 2026): under WarpMode.Consensus KSP's own "warp to" (the space
    /// centre's sunrise button, the map's apoapsis, periapsis, manoeuvre and "warp here", Principia's warp to
    /// manoeuvre, Kerbal Alarm Clock's warp to alarm: all call TimeWarp.WarpTo) does not warp this game
    /// alone; it opens the group warp request filled in with that exact time (Jacob).
    /// </summary>
    [HarmonyPatch(typeof(TimeWarp))]
    [HarmonyPatch("WarpTo")]
    public class TimeWarp_WarpTo
    {
        [HarmonyPrefix]
        private static bool Prefix(double __0)
        {
            if (!PimpConsensusFollower.Active || PimpConsensusFollower.Driving) return true;
            PimpConsensusUi.RequestTo(__0, Source());
            return false;
        }

        // Who asked: the first caller outside KSP's TimeWarp and Harmony, as a hint for the window.
        private static string Source()
        {
            try
            {
                foreach (var f in new StackTrace(2, false).GetFrames() ?? new StackFrame[0])
                {
                    var t = f.GetMethod()?.DeclaringType;
                    if (t == null || t == typeof(TimeWarp) || t.Namespace == "HarmonyLib" || t.FullName.StartsWith("LmpClient")) continue;
                    var ns = t.Namespace ?? "";
                    if (ns.StartsWith("principia")) return "(Principia)";
                    if (ns.StartsWith("KerbalAlarmClock")) return "(alarm)";
                    return "(" + t.Name + ")";
                }
            }
            catch { }
            return "";
        }
    }
}
