using HarmonyLib;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.Warp;
using LmpCommon.Enums;
using System;
using System.Reflection;
using UnityEngine;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP warp guard (warp plan W2, 24 Oct 2026): moved here unchanged from the bridge client mod's
    /// WarpGuard (PrincipiaLmpBridge 0.2.0-0.4.8). Makes WarpMode None actually hold.
    ///
    /// Why (architecture doc, "Live LMP tests", finding 5): under WarpMode None a plain tap of the warp
    /// key still warped. KSP's TimeWarp.Update calls its internal TimeWarp.setRate directly, while LMP
    /// patches only the public SetRate(int, bool, bool) and setMode; LMP then noticed the warp, tried
    /// SetRate(0), which its own patch refused, and set subspace -1, and under WarpMode None nothing
    /// brought the client back. So:
    ///   a. block at the source: a prefix on KSP's internal setRate refuses any rate above 1x
    ///      (<see cref="TimeWarp_setRateInternal"/>);
    ///   b. LMP's SetRate prefix always lets SetRate(0) through, so a cancel works whatever started the
    ///      warp (TimeWarp_SetRate.PrefixSetRate);
    ///   c. once a warp has stopped, put the client back on the server's subspace (<see cref="Update"/>).
    /// Active only when connected to a server with WarpMode None.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class PimpWarpGuard : MonoBehaviour
    {
        private const string LogPrefix = "[LMP][PiMP] ";
        private static float _nextMessage;

        // W4.2: also under WarpMode.Consensus, where only the group warp drives KSP's warp
        // (PimpConsensusFollower.Driving).
        internal static bool Active =>
            MainSystem.NetworkState >= ClientState.Connected &&
            (SettingsSystem.ServerSettings.WarpMode == WarpMode.None || SettingsSystem.ServerSettings.WarpMode == WarpMode.Consensus);

        public void Awake() => DontDestroyOnLoad(this);

        internal static bool AllowRate(int rateIndex)
        {
            if (rateIndex <= 0 || !Active || PimpConsensusFollower.Driving) return true;
            if (Time.realtimeSinceStartup >= _nextMessage)
            {
                ScreenMessages.PostScreenMessage(SettingsSystem.ServerSettings.WarpMode == WarpMode.Consensus
                    ? "Time warp on this server is a group warp" : "Time warp is disabled on this server", 2f, ScreenMessageStyle.UPPER_CENTER);
                _nextMessage = Time.realtimeSinceStartup + 2f;
            }
            return false;
        }

        public void Update()
        {
            if (!Active || WarpSystem.Singleton == null) return;
            var warp = WarpSystem.Singleton;
            if (warp.CurrentSubspace != -1) return;
            if (TimeWarp.CurrentRateIndex != 0 || Math.Abs(TimeWarp.CurrentRate - 1f) > 0.1f) return;
            var target = warp.LatestSubspace;
            LunaLog.Log(LogPrefix + "Warp stopped; returning from subspace -1 to subspace " + target + ".");
            warp.CurrentSubspace = target;
        }
    }

    /// <summary>Part a: KSP's internal TimeWarp.setRate (the warp keys) refuses rates above 1x under WarpMode None.</summary>
    [HarmonyPatch]
    public class TimeWarp_setRateInternal
    {
        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(TimeWarp), "setRate");

        [HarmonyPrefix]
        private static bool Prefix(int __0) => PimpWarpGuard.AllowRate(__0);
    }
}
