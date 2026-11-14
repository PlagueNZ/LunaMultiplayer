using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP manoeuvre node guard (14 Nov 2026). Manoeuvre nodes made in one session were gone in the
    /// session after next. Why, from KSP's own code: when a vessel becomes the active one, its
    /// PatchedConicSolver reads the saved plan into a private list (flightPlanLoadList) and its Start
    /// coroutine then adds ONE node per frame to maneuverNodes, raising no event. A backup of the
    /// vessel in those frames (ProtoVessel's constructor) clears Vessel.flightPlanNode and saves the
    /// solver's still-empty node list over it. LMP sends the active vessel at flight ready
    /// (VesselProtoEvents.FlightReady), inside that window, so the server's copy lost its plan; the
    /// nodes still showed in that session, from the list, and were gone at the next join.
    /// So: while any node in the load list has not been handed to the solver yet (its solver field is
    /// still null), a backup keeps the plan the vessel had before it.
    /// </summary>
    internal static class PimpFlightPlanGuard
    {
        private static readonly FieldInfo LoadList = AccessTools.Field(typeof(PatchedConicSolver), "flightPlanLoadList");
        private static bool _warned;

        /// <summary>A copy of the vessel's saved plan if KSP is still loading it into the solver, else null.</summary>
        public static ConfigNode PlanStillLoading(Vessel vessel)
        {
            try
            {
                if (vessel == null || !vessel.PatchedConicsAttached || vessel.patchedConicSolver == null) return null;
                if (vessel.flightPlanNode == null || !vessel.flightPlanNode.HasNode("MANEUVER")) return null;
                if (LoadList == null)
                {
                    if (!_warned) LunaLog.LogWarning("[LMP]: [PiMP] PatchedConicSolver.flightPlanLoadList not found; manoeuvre nodes are not guarded");
                    _warned = true;
                    return null;
                }

                var pending = LoadList.GetValue(vessel.patchedConicSolver) as List<ManeuverNode>;
                if (pending == null) return null;
                foreach (var node in pending)
                    if (node != null && node.solver == null) return vessel.flightPlanNode.CreateCopy();
                return null;
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: [PiMP] Manoeuvre node guard failed: {e.Message}");
                return null;
            }
        }

        /// <summary>Puts the plan back on the vessel and into the backup just made.</summary>
        public static void Restore(Vessel vessel, ProtoVessel backup, ConfigNode plan)
        {
            try
            {
                vessel.flightPlanNode = plan.CreateCopy();
                if (backup != null) backup.flightPlan = plan.CreateCopy();
                LunaLog.Log($"[LMP]: [PiMP] Kept {plan.GetNodes("MANEUVER").Length} manoeuvre node(s) of {vessel.vesselName}: backed up while KSP was still loading them");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: [PiMP] Manoeuvre node guard could not restore the plan: {e.Message}");
            }
        }
    }
}
