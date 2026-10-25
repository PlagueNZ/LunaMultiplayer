using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.TimeSync;
using LmpClient.Systems.Warp;
using LmpCommon.Enums;
using LmpCommon.Pimp;
using LmpCommon.Time;
using System;
using System.Globalization;
using UnityEngine;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP consensus warp (plan W4.2, 26 Oct 2026): this game follows the server's warp clock.
    /// Under WarpMode.Consensus everyone stays in one subspace and the server moves its time at the
    /// warp's rate (LmpCommon.Pimp.PimpWarpClock, W4.1). WarpSystem.GetSubspaceTime follows the clock,
    /// so LMP's own clock sync (with PiMP's clock policy) holds this game to it; this addon sets KSP's
    /// warp so the game can keep up:
    ///   - while the warp runs: the highest on-rails rate in this game's table at or below the clock's,
    ///     as far as KSP allows here (GetMaxRateForAltitude in flight); a game behind the clock is
    ///     jumped forward by the clock sync (on rails, the vessels are packed);
    ///   - within LandWindowS real seconds of the target: lands itself, dropping the rate each frame so it
    ///     stops just short of the target (KSP's WarpTo overshot by 15 s at 1000x), never past the time
    ///     everyone lands on (Principia cannot go back);
    ///   - once landed: back to 1x.
    /// Players' own warp keys stay blocked under Consensus (PimpWarpGuard); only this addon drives the
    /// warp (<see cref="Driving"/>). Proposals and votes are W4.3; physics warp later.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class PimpConsensusFollower : MonoBehaviour
    {
        private const string LogPrefix = "[LMP][PiMP] ";
        private const double LandWindowS = 3;
        private const double LandStopS = 0.5;
        private const float LogIntervalS = 5f;

        /// <summary>True while this addon changes KSP's warp rate (PimpWarpGuard lets it through).</summary>
        internal static bool Driving;

        private static bool _landing;

        /// <summary>W4.3: landing on a group warp's target. LMP's clock sync stands aside meanwhile
        /// (PimpClockPolicy): a forward jump in the same frame as a high warp step carried the game 200 s
        /// past the target at 100,000x (26 Oct).</summary>
        internal static bool Landing => _landing && Active;
        private bool _following;
        private float _nextLog;
        private PimpWarpClock _clock;
        private ScreenMessage _banner;
        private float _nextBanner;

        internal static bool Active =>
            MainSystem.NetworkState >= ClientState.Connected &&
            SettingsSystem.ServerSettings.WarpMode == WarpMode.Consensus;

        public void Awake() => DontDestroyOnLoad(this);

        public void Update()
        {
            try { Follow(); }
            catch (Exception e) { LunaLog.LogError(LogPrefix + "Consensus warp follower: " + e); _landing = false; Driving = false; }
        }

        private void Follow()
        {
            if (!Active || !HighLogic.LoadedSceneIsGame || TimeWarp.fetch == null || WarpSystem.Singleton == null)
            {
                _following = false; _landing = false; Driving = false;
                return;
            }
            var c = WarpSystem.Singleton.PimpWarpClock;
            var now = LunaNetworkTime.UtcNow.Ticks;
            var start = TimeSyncSystem.ServerStartTime;
            var warping = c != null && c.IsWarping && !c.HasLanded(now, start) && c.SubspaceKey == WarpSystem.Singleton.CurrentSubspace;
            var ut = Planetarium.GetUniversalTime();

            if (!warping)
            {
                if (_following)
                {
                    _following = false;
                    LunaLog.Log(LogPrefix + "Consensus warp over: UT " + F(ut) + (c != null ? ", clock " + F(c.UniverseTime(now, start)) : "") + ".");
                }
                // The clock has landed: back to 1x at once (a game short of the target is closed by the clock sync).
                _landing = false;
                if (TimeWarp.CurrentRateIndex > 0) SetRate(0, true);
                Driving = false;
                ClearBanner();
                return;
            }

            if (!_following || !ReferenceEquals(c, _clock))
            {
                _following = true;
                _clock = c;
                LunaLog.Log(LogPrefix + "Consensus warp: following the server's clock (" + c + "); UT " + F(ut) + ", clock " + F(c.UniverseTime(now, start)) + ".");
            }
            Banner(c, ut);

            // Last few real seconds: land on the target ourselves. KSP's WarpTo is not exact at high
            // rates (26 Oct, pimp.6 test: "Overshoot is 15.4s" at 1000x, 16.6 s past the target), and
            // Principia cannot go back. Each frame: the highest rate that cannot reach the target
            // within two frames, changed instantly; 1x once inside half a second. The clock sync
            // closes what is left from behind.
            if (!double.IsNaN(c.Target) && c.Target - ut <= c.Rate * LandWindowS)
            {
                if (!_landing)
                {
                    _landing = true;
                    LunaLog.Log(LogPrefix + "Consensus warp: landing on " + F(c.Target) + " (UT " + F(ut) + ").");
                }
                Driving = true;
                var remaining = c.Target - ut;
                EnsureMode(c.Physics);
                var landIdx = remaining <= LandStopS ? 0 : LandingIndex(c.Rate, remaining, c.Physics);
                if (TimeWarp.CurrentRateIndex != landIdx) SetRate(landIdx, true);
                return;
            }

            EnsureMode(c.Physics);
            var idx = RailsIndexFor(c.Rate, c.Physics);
            if (TimeWarp.CurrentRateIndex != idx) SetRate(idx);
            Report(c.Physics);

            if (Time.realtimeSinceStartup >= _nextLog)
            {
                _nextLog = Time.realtimeSinceStartup + LogIntervalS;
                LunaLog.Log(LogPrefix + "Consensus warp: UT " + F(ut) + ", clock " + F(c.UniverseTime(now, start)) + " (" + F(ut - c.UniverseTime(now, start)) +
                            " s), KSP rate " + TimeWarp.CurrentRate.ToString("G6", CultureInfo.InvariantCulture) + "x (index " + TimeWarp.CurrentRateIndex + "), clock rate " + c.Rate.ToString("G6", CultureInfo.InvariantCulture) + "x.");
            }
        }

        // While landing: the highest index at or below the clock's rate that covers at most half the
        // remaining game time in two frames.
        private static int LandingIndex(double rate, double remaining, bool physics)
        {
            var rates = physics ? TimeWarp.fetch.physicsWarpRates : TimeWarp.fetch.warpRates;
            var frame = Math.Max(Time.unscaledDeltaTime, 0.02f);
            var cap = RailsIndexFor(rate, physics);
            var idx = 0;
            for (var i = 1; i <= cap && i < rates.Length; i++) if (rates[i] * frame * 2 <= remaining * 0.5) idx = i;
            return idx;
        }

        // Jacob (26 Oct): the group warp's text stays up for the whole warp.
        private void Banner(PimpWarpClock c, double ut)
        {
            if (Time.realtimeSinceStartup < _nextBanner) return;
            _nextBanner = Time.realtimeSinceStartup + 0.5f;
            var mine = c.Proposer == SettingsSystem.CurrentSettings.PlayerName;
            var text = "Group warp " + c.Rate.ToString("N0", CultureInfo.InvariantCulture) + "x" +
                       (double.IsNaN(c.Limit) ? "" : " (max " + c.Limit.ToString("N0", CultureInfo.InvariantCulture) + "x)") +
                       (double.IsNaN(c.Target)
                           ? " until stopped"
                           : " to " + KSPUtil.PrintDateCompact(c.Target, true, true) + " (" + Math.Max(0, (c.Target - ut) / c.Rate).ToString("F0", CultureInfo.InvariantCulture) + " s)") +
                       (mine ? " · your warp keys change it, " + GameSettings.TIME_WARP_STOP.primary + " stops it"
                             : c.Proposer.Length > 0 ? " · asked by " + c.Proposer : "");
            if (_banner != null) ScreenMessages.RemoveMessage(_banner);
            _banner = ScreenMessages.PostScreenMessage(text, 1.2f, ScreenMessageStyle.UPPER_CENTER);
        }

        private void ClearBanner()
        {
            if (_banner == null) return;
            ScreenMessages.RemoveMessage(_banner);
            _banner = null;
        }

        // The highest on-rails index whose rate is at most the clock's, capped by KSP's limits here.
        private static int RailsIndexFor(double rate, bool physics = false)
        {
            var rates = physics ? TimeWarp.fetch.physicsWarpRates : TimeWarp.fetch.warpRates;
            var idx = 0;
            for (var i = 1; i < rates.Length; i++) if (rates[i] <= rate * 1.0001) idx = i;
            if (physics) return idx;   // physics warp has no altitude limits
            // In flight, KSP's altitude limits for the active vessel (KSP also refuses a rate it does not allow).
            var v = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            var max = v != null && v.mainBody != null ? TimeWarp.fetch.GetMaxRateForAltitude(v.altitude, v.mainBody) : rates.Length - 1;
            return Math.Max(0, Math.Min(idx, max));
        }

        // W4.3: physics warp is KSP's low mode (TimeWarp.setMode is private).
        private static System.Reflection.MethodInfo _setMode;
        private static void EnsureMode(bool physics)
        {
            var want = physics ? TimeWarp.Modes.LOW : TimeWarp.Modes.HIGH;
            if (TimeWarp.fetch.Mode == want) return;
            if (_setMode == null) _setMode = HarmonyLib.AccessTools.Method(typeof(TimeWarp), "setMode");
            if (TimeWarp.CurrentRateIndex > 0) SetRate(0, true);
            Driving = true;
            try { _setMode?.Invoke(TimeWarp.fetch, new object[] { want }); }
            finally { Driving = _landing; }
            LunaLog.Log(LogPrefix + "Consensus warp: KSP warp mode " + want + ".");
        }

        // W4.3: every 2 s during a group warp, this game's rate limit and next alarm, so the server can
        // lower the rate or move the target to an alarm.
        private float _nextReport;
        private void Report(bool physics)
        {
            if (Time.realtimeSinceStartup < _nextReport) return;
            _nextReport = Time.realtimeSinceStartup + 2f;
            var msg = Network.NetworkMain.CliMsgFactory.CreateNewMessageData<LmpCommon.Message.Data.Warp.WarpReportMsgData>();
            msg.Report.CopyFrom(PimpWarpReporter.Local(physics));
            WarpSystem.Singleton.MessageSender.SendMessage(msg);
        }

        private static void SetRate(int idx, bool instant = false)
        {
            Driving = true;
            try { TimeWarp.SetRate(idx, instant, false); }
            finally { Driving = _landing; }
        }

        private static string F(double x) => x.ToString("F1", CultureInfo.InvariantCulture);
    }
}
