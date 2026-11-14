using System;
using System.Collections.Generic;
using UnityEngine;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP (14 Nov 2026): manoeuvre nodes survive leaving flight. KSP's PatchedConicRenderer deletes
    /// every node of its vessel when a scene load is requested (OnSceneSwitch, a handler of
    /// onGameSceneLoadRequested) and again in its OnDestroy, each with ManeuverNode.RemoveSelf, which
    /// raises onManeuverRemoved. Stock KSP has saved the game just before ("Flight State Captured"),
    /// so its save keeps them. LMP has no such save: its handler sent the vessel with the now-empty
    /// plan, the server stored it, and the nodes were gone at the next join (Jacob's Steam install,
    /// 14 Nov: "removed by ... ManeuverNode.RemoveSelf &lt;- PatchedConicRenderer.OnSceneSwitch").
    /// Now, just before KSP clears a vessel's nodes, its plan is copied; for the next 30 s, or until
    /// another scene has loaded, LMP does not send for a node removal or a node change, and a backup
    /// of that vessel gets the copied plan.
    /// </summary>
    internal static class PimpNodeTeardown
    {
        private const float WindowS = 30f;
        private static readonly Dictionary<Guid, ConfigNode> Plans = new Dictionary<Guid, ConfigNode>();
        private static float _startedAt = -1000f;
        private static GameScenes _scene;

        /// <summary>True while KSP is clearing nodes for a scene change.</summary>
        public static bool Active => Time.realtimeSinceStartup - _startedAt < WindowS && HighLogic.LoadedScene == _scene;

        public static void Begin(PatchedConicRenderer renderer)
        {
            try
            {
                if (!Active) Plans.Clear();
                _startedAt = Time.realtimeSinceStartup;
                _scene = HighLogic.LoadedScene;

                var solver = renderer?.solver;
                var vessel = solver?.obtDriver?.vessel;
                if (vessel == null || solver.maneuverNodes == null || solver.maneuverNodes.Count == 0) return;
                if (Plans.ContainsKey(vessel.id)) return;   // the first copy, before any node is gone

                var plan = new ConfigNode("FLIGHTPLAN");
                solver.Save(plan);
                Plans[vessel.id] = plan;
                LunaLog.Log($"[LMP]: [PiMP] Keeping {solver.maneuverNodes.Count} manoeuvre node(s) of {vessel.vesselName}: KSP clears them as flight ends");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: [PiMP] Manoeuvre node copy at scene change failed: {e.Message}");
            }
        }

        /// <summary>The plan copied for this vessel during the current teardown, or null.</summary>
        public static ConfigNode PlanFor(Vessel vessel)
        {
            if (!Active || vessel == null) return null;
            return Plans.TryGetValue(vessel.id, out var plan) ? plan : null;
        }
    }
}
