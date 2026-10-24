using LmpClient.Systems.TimeSync;
using LmpClient.Systems.Warp;
using LmpCommon.Enums;
using System;
using UnityEngine;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP clock policy (warp plan W2, 24 Oct 2026): moved here unchanged from the bridge client
    /// mod's ClockPolicy (PrincipiaLmpBridge 0.3.0-0.4.8), which patched TimeSyncSystem from outside.
    /// TimeSyncSystem.CheckGameTime now asks <see cref="BeforeCheck"/> first, and SetGameTime calls
    /// <see cref="CountJump"/>.
    ///
    /// Why (architecture doc, "Live LMP tests", finding 4): CheckGameTime runs every physics frame.
    /// Within 3.5 s of the server it nudges Time.timeScale (0.85-1.20x); beyond that it hard-jumps the
    /// clock with Planetarium.SetUniversalTime. Forward jumps mid-burn pack the active vessel onto rails
    /// (lost thrust, throttle cut); backward jumps freeze Principia, which refuses to go back in time.
    ///
    /// Policy, applied only beyond the 3.5 s limit (below it LMP is unchanged):
    ///   - behind and UNSAFE (an engine producing thrust, or in atmosphere and not landed, or within
    ///     10 s of a separation, or not yet 5 s continuously safe, or the vessel loaded and within
    ///     60 s): hold the jump and run at 1.2x;
    ///   - behind and safe, or the active vessel packed: let LMP jump, as before;
    ///   - ahead: never jump backwards; run at 0.5x until back within 3.5 s.
    /// Also logs the clock every 10 s in flight (error, game rate, holding).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class PimpClockPolicy : MonoBehaviour
    {
        private const string LogPrefix = "[LMP][PiMP] ";
        private const double LmpJumpLimitS = 3.5;   // TimeSyncSystem.MaxPhysicsClockMsError
        private const double ReleaseBelowS = 3.0;
        private const float CatchUpRate = 1.20f;    // TimeSyncSystem.MaxPhysicsClockRate
        private const float SlowDownRate = 0.5f;
        private const float LogIntervalS = 10f;
        private const double MaxCatchUpS = 60.0;
        private const float SeparationQuietS = 10f;
        private const float SafeForS = 5f;
        private enum VesselClock { None, Packed, Unpacked }

        private static bool _holding;
        private static string _holdReason;
        private static double _maxHeldS;
        private static float _nextMessage;
        private static float _nextUnsafeCheck;
        private static string _unsafeReason;
        private static float _lastSeparation = -1000f;
        private static float _safeSince = -1f;
        private static int _jumpCount;

        private float _nextLog;
        private double _lastLogUt;
        private float _lastLogReal;
        private int _jumpsSinceLog;

        public void Awake()
        {
            DontDestroyOnLoad(this);
            GameEvents.onPartDeCouple.Add(OnPartSeparated);
            GameEvents.onPartUndock.Add(OnPartSeparated);
            GameEvents.onStageSeparation.Add(OnStageSeparated);
            GameEvents.onVesselCreate.Add(OnVesselCreated);
            LunaLog.Log(LogPrefix + "Clock policy active (no hard clock jumps under thrust or in atmosphere; never backwards; " +
                        "clock logged every " + LogIntervalS + " s in flight).");
        }

        public void OnDestroy()
        {
            GameEvents.onPartDeCouple.Remove(OnPartSeparated);
            GameEvents.onPartUndock.Remove(OnPartSeparated);
            GameEvents.onStageSeparation.Remove(OnStageSeparated);
            GameEvents.onVesselCreate.Remove(OnVesselCreated);
        }

        private void OnPartSeparated(Part p) => MarkSeparation();
        private void OnStageSeparated(EventReport r) => MarkSeparation();
        private void OnVesselCreated(Vessel v) => MarkSeparation();
        private static void MarkSeparation() { _lastSeparation = Time.realtimeSinceStartup; }

        private static bool Connected => MainSystem.NetworkState >= ClientState.Connected;

        private static bool LmpWouldSync
        {
            get
            {
                if (!Connected || WarpSystem.Singleton.CurrentSubspace == -1 ||
                    WarpSystem.Singleton.WaitingSubspaceIdFromServer) return false;
                switch (HighLogic.LoadedScene)
                {
                    case GameScenes.TRACKSTATION:
                    case GameScenes.FLIGHT:
                    case GameScenes.SPACECENTER:
                    case GameScenes.EDITOR:
                        return true;
                    default:
                        return false;
                }
            }
        }

        // Reason a hard jump would hurt the active vessel now, or null if safe. At most every 0.25 s.
        private static string UnsafeReason()
        {
            if (Time.realtimeSinceStartup < _nextUnsafeCheck) return _unsafeReason;
            _nextUnsafeCheck = Time.realtimeSinceStartup + 0.25f;
            _unsafeReason = null;

            if (!HighLogic.LoadedSceneIsFlight) return null;
            var v = FlightGlobals.ActiveVessel;
            if (v == null || !v.loaded || v.packed) return null;

            var body = v.mainBody;
            if (!v.LandedOrSplashed && body != null && body.atmosphere && v.altitude < body.atmosphereDepth)
                return _unsafeReason = "in atmosphere";

            foreach (var part in v.parts)
                foreach (var module in part.Modules)
                    if (module is ModuleEngines e && e.EngineIgnited && e.finalThrust > 0f)
                        return _unsafeReason = "engine thrusting";
            return null;
        }

        private static VesselClock ActiveVesselClock()
        {
            if (!HighLogic.LoadedSceneIsFlight) return VesselClock.None;
            var v = FlightGlobals.ActiveVessel;
            if (v == null) return VesselClock.None;
            return !v.loaded || v.packed ? VesselClock.Packed : VesselClock.Unpacked;
        }

        // Safe must be sustained: a jump in the same frame as a decouple packed the fresh debris before
        // Principia had processed the split, and Principia aborted (3 Oct). So no jump within 10 s of a
        // separation, and only after 5 s continuously out of thrust and atmosphere.
        private static string SustainedUnsafeReason()
        {
            var now = Time.realtimeSinceStartup;
            var reason = UnsafeReason();
            if (reason == null && HighLogic.LoadedSceneIsFlight && now - _lastSeparation < SeparationQuietS)
                reason = "recent staging";
            if (reason != null) { _safeSince = -1f; return reason; }
            if (_safeSince < 0f) _safeSince = now;
            if (HighLogic.LoadedSceneIsFlight && now - _safeSince < SafeForS) return "settling";
            return null;
        }

        /// <summary>Called first in TimeSyncSystem.CheckGameTime. False: skip LMP's check this frame.</summary>
        public static bool BeforeCheck()
        {
            try
            {
                if (!LmpWouldSync) { EndHold(null); return true; }

                var errorS = TimeSyncSystem.CurrentErrorSec;   // < 0: behind the server
                var limit = _holding ? ReleaseBelowS : LmpJumpLimitS;
                if (Math.Abs(errorS) <= limit) { EndHold(errorS); return true; }
                if (_holding && errorS < 0 && -errorS <= LmpJumpLimitS)
                {
                    Time.timeScale = CatchUpRate;
                    return false;
                }

                if (errorS > 0)
                {
                    Time.timeScale = SlowDownRate;   // ahead: never jump backwards
                    Notify("Clock " + errorS.ToString("F1") + " s ahead of server; slowing to resync");
                    return false;
                }

                // Active vessel packed (e.g. the first seconds after the flight scene loads): jump at
                // once, like a short time warp. Unpacked: no hard jump within 60 s; catch up at 1.2x.
                var vesselClock = ActiveVesselClock();
                if (vesselClock == VesselClock.Packed)
                {
                    if (!_holding)
                        LunaLog.Log(LogPrefix + "Clock sync with the active vessel packed: letting LMP jump " +
                                    (-errorS).ToString("F1") + " s now.");
                    EndHold(errorS);
                    return true;
                }
                var reason = SustainedUnsafeReason();
                if (reason == null && vesselClock == VesselClock.Unpacked && -errorS <= MaxCatchUpS)
                    reason = "vessel loaded";
                if (reason == null)
                {
                    EndHold(errorS);
                    return true;
                }

                if (!_holding)
                {
                    _holding = true;
                    _maxHeldS = 0;
                    LunaLog.Log(LogPrefix + "Holding clock sync (" + reason + "): " + (-errorS).ToString("F1") + " s behind server.");
                }
                _holdReason = reason;
                _maxHeldS = Math.Max(_maxHeldS, -errorS);
                Time.timeScale = CatchUpRate;
                Notify((-errorS).ToString("F0") + " s behind server (" + reason + "); " +
                       (reason == "vessel loaded" ? "catching up at 1.2x" : "will sync when coasting"));
                return false;
            }
            catch (Exception e)
            {
                LunaLog.LogError(LogPrefix + "Clock policy error; LMP's sync applies. " + e);
                return true;
            }
        }

        /// <summary>Called at the start of TimeSyncSystem.SetGameTime: counts LMP's hard jumps for the log.</summary>
        public static void CountJump() { _jumpCount++; }

        private static void EndHold(double? errorS)
        {
            if (!_holding) return;
            _holding = false;
            LunaLog.Log(LogPrefix + "Releasing clock sync hold: now " +
                        (errorS.HasValue ? (-errorS.Value).ToString("F1") + " s behind" : "not syncing") +
                        ", max held " + _maxHeldS.ToString("F1") + " s.");
        }

        private static void Notify(string text)
        {
            if (Time.realtimeSinceStartup < _nextMessage) return;
            _nextMessage = Time.realtimeSinceStartup + 1f;
            ScreenMessages.PostScreenMessage(text, 1.1f, ScreenMessageStyle.UPPER_CENTER);
        }

        // Every 10 s in flight: the error and the game's actual rate (jumps included and counted).
        public void Update()
        {
            if (!Connected || !HighLogic.LoadedSceneIsFlight)
            {
                _lastLogReal = 0;
                return;
            }
            var now = Time.realtimeSinceStartup;
            var ut = Planetarium.GetUniversalTime();
            if (_lastLogReal == 0)
            {
                _lastLogReal = now; _lastLogUt = ut; _nextLog = now + LogIntervalS; _jumpsSinceLog = _jumpCount;
                return;
            }
            if (now < _nextLog) return;

            var rate = (ut - _lastLogUt) / Math.Max(0.001, now - _lastLogReal);
            var jumps = _jumpCount - _jumpsSinceLog;
            LunaLog.Log(LogPrefix + "Clock: error " + TimeSyncSystem.CurrentErrorSec.ToString("F2") +
                        " s, game rate " + rate.ToString("F3") + "x over " + (now - _lastLogReal).ToString("F1") +
                        " s (" + jumps + " LMP jumps), timeScale " + Time.timeScale.ToString("F2") +
                        (_holding ? ", HOLDING (" + _holdReason + ")" : "") + ".");
            _lastLogReal = now; _lastLogUt = ut; _nextLog = now + LogIntervalS; _jumpsSinceLog = _jumpCount;
        }
    }
}
