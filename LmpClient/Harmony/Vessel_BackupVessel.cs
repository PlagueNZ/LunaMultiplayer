using HarmonyLib;
using LmpClient.Pimp;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// PiMP (14 Nov 2026): keep a vessel's manoeuvre nodes when it is backed up while KSP is still
    /// loading them (<see cref="PimpFlightPlanGuard"/>), or while KSP clears them as flight ends
    /// (<see cref="PimpNodeTeardown"/>).
    /// </summary>
    [HarmonyPatch(typeof(Vessel))]
    [HarmonyPatch("BackupVessel")]
    public class Vessel_BackupVessel
    {
        [HarmonyPrefix]
        private static void PrefixBackupVessel(Vessel __instance, out ConfigNode __state)
        {
            __state = PimpFlightPlanGuard.PlanStillLoading(__instance);
        }

        [HarmonyPostfix]
        private static void PostfixBackupVessel(Vessel __instance, ProtoVessel __result, ConfigNode __state)
        {
            if (__state != null) PimpFlightPlanGuard.Restore(__instance, __result, __state);
            // PiMP (14 Nov 2026): while KSP clears the nodes as flight ends, back up the plan it had.
            var kept = PimpNodeTeardown.PlanFor(__instance);
            if (kept != null && __result != null) __result.flightPlan = kept.CreateCopy();
        }
    }
}
