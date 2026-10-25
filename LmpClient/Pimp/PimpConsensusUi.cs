using LmpClient.Network;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.TimeSync;
using LmpClient.Systems.Warp;
using LmpCommon.Message.Data.Warp;
using LmpCommon.Time;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using UnityEngine;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP consensus warp (plan W4.3, 26 Oct 2026): the request window, the vote popup and the results
    /// (mock-ups agreed with Jacob, 25 Oct; "the popup will change with design choices").
    ///   - Under WarpMode.Consensus the warp-up key (TIME_WARP_INCREASE) opens the request window:
    ///     until I stop / to a date / for a duration; rails or physics; a rate or the highest everyone
    ///     can use. "Ask everyone" sends WarpPropose with this game's report.
    ///   - The server's WarpAsk opens the popup: who asked, for what, this game's own report (can it
    ///     warp, up to what rate), a countdown; only Yes counts, and Yes is greyed out when this game
    ///     cannot take part.
    ///   - The warp-stop key (TIME_WARP_STOP) during a group warp asks the server to stop it (only the
    ///     proposer may; the server says so otherwise).
    ///   - WarpResult texts are shown on screen for a few seconds.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class PimpConsensusUi : MonoBehaviour
    {
        private const string LogPrefix = "[LMP][PiMP] ";
        private static readonly DateTime CalendarStart = new DateTime(1951, 1, 1, 0, 0, 0, DateTimeKind.Utc);   // UT 0 (PiMP's calendar)
        // Jacob (26 Oct): a start rate; the proposer changes it during the warp with the warp keys or buttons.
        private static readonly double[] RateChoices = { 10, 100, 1000, 10000, 100000 };
        private static readonly ConcurrentQueue<Action> Pending = new ConcurrentQueue<Action>();

        private PopupDialog _request, _vote;
        private int _voteId;
        private float _voteDeadline;

        // Request window state.
        private PimpWarpRequestMode _mode = PimpWarpRequestMode.UntilStopped;
        private bool _physics;
        private int _rateChoice = 2;   // 1,000x
        private string _dateText = "", _hoursText = "6", _minutesText = "0";
        // W4.3 (Jacob, 26 Oct): a KSP "warp to" (sunrise, apoapsis, periapsis, manoeuvre, alarm) fills the
        // window with its exact time; kept to the second while the date box still shows it.
        private double _exactTarget = double.NaN;
        private string _exactText = "", _source = "";
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>Called from the warp message handler (network thread); runs on the Unity thread.</summary>
        internal static void Post(Action a) => Pending.Enqueue(a);

        public void Awake() => DontDestroyOnLoad(this);

        public void Update()
        {
            while (Pending.TryDequeue(out var a))
            {
                try { a(); } catch (Exception e) { LunaLog.LogError(LogPrefix + "Consensus warp UI: " + e); }
            }
            if (!PimpConsensusFollower.Active || !HighLogic.LoadedSceneIsGame) { Close(ref _request); Close(ref _vote); return; }
            if (_vote != null && Time.realtimeSinceStartup > _voteDeadline) Close(ref _vote);

            if (_openRequest) { _openRequest = false; if (_request == null && _vote == null) OpenRequest(); }
        }

        private bool _openRequest;
        private static float _nextPlayerRate, _nextNotice;

        private static void Notice(string text)
        {
            if (Time.realtimeSinceStartup < _nextNotice) return;
            _nextNotice = Time.realtimeSinceStartup + 1.5f;
            ScreenMessages.PostScreenMessage(text, 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>
        /// A warp key or a time-gauge button under Consensus (from PimpWarpGuard; KSP's own setRate). Not
        /// warping: opens the request window. During a group warp: the proposer changes its rate (up to the
        /// group limit) or stops it (rate 0); anyone else is told who can. Returns false: KSP's own warp
        /// never changes; the follower applies the server's clock.
        /// </summary>
        internal static bool OnPlayerRate(int idx)
        {
            var c = WarpSystem.Singleton?.PimpWarpClock;
            var warping = c != null && c.IsWarping && !c.HasLanded(LunaNetworkTime.UtcNow.Ticks, TimeSyncSystem.ServerStartTime);
            if (!warping)
            {
                if (idx <= 0) return true;   // KSP's own "back to 1x" when nothing runs
                if (Instance != null) Instance._openRequest = true;
                return false;
            }
            if (c.Proposer != SettingsSystem.CurrentSettings.PlayerName)
            {
                Notice("Only " + c.Proposer + " can change or stop this group warp");
                return false;
            }
            if (Time.realtimeSinceStartup < _nextPlayerRate) return false;
            _nextPlayerRate = Time.realtimeSinceStartup + 0.25f;
            if (idx <= 0)
            {
                Send(NetworkMain.CliMsgFactory.CreateNewMessageData<WarpStopRequestMsgData>());
                Notice("Stopping the group warp");
                return false;
            }
            var rates = c.Physics ? TimeWarp.fetch.physicsWarpRates : TimeWarp.fetch.warpRates;
            double r = rates[Math.Min(idx, rates.Length - 1)];
            if (!double.IsNaN(c.Limit) && r > c.Limit) { r = c.Limit; Notice("Group warp: the fastest everyone can use is " + F(c.Limit, "N0") + "x"); }
            if (Math.Abs(r - c.Rate) < 1e-6 * r) return false;
            var msg = NetworkMain.CliMsgFactory.CreateNewMessageData<WarpRateRequestMsgData>();
            msg.Rate = r;
            Send(msg);
            LunaLog.Log(LogPrefix + "Group warp: asked for " + F(r, "N0") + "x.");
            return false;
        }

        private static void Send(LmpCommon.Message.Interface.IMessageData msg) => WarpSystem.Singleton.MessageSender.SendMessage(msg);

        private static void Close(ref PopupDialog d)
        {
            try { if (d != null) d.Dismiss(); } catch { /* destroyed with its scene */ }
            d = null;
        }

        private static string F(double x, string f = "F0") => x.ToString(f, CultureInfo.InvariantCulture);
        private static string Date(double ut) => KSPUtil.PrintDateCompact(ut, true, true);
        private static string RateText(double r) => r <= 0 ? "Highest everyone can use" : F(r, "N0") + "x";

        // ----- the request window -----

        private void OpenRequest()
        {
            var ut = Planetarium.GetUniversalTime();
            if (_dateText == "") _dateText = CalendarStart.AddSeconds(ut + 86400).ToString(DateFormat, CultureInfo.InvariantCulture);
            var dialog = new MultiOptionDialog("PimpWarpRequest", "", "Time warp", HighLogic.UISkin, 380f,
                new DialogGUIVerticalLayout(
                    new DialogGUIToggle(() => _mode == PimpWarpRequestMode.ToTime, () => "Warp to" + (_source.Length > 0 && _dateText == _exactText ? " " + _source : "") + " (yyyy-MM-dd HH:mm:ss)", on => { if (on) _mode = PimpWarpRequestMode.ToTime; }, 360f, 24f),
                    new DialogGUITextInput(_dateText, false, 19, s => _dateText = s, 360f, 24f),
                    new DialogGUIToggle(() => _mode == PimpWarpRequestMode.ForDuration, "Warp for (hours, minutes)", on => { if (on) _mode = PimpWarpRequestMode.ForDuration; }, 360f, 24f),
                    new DialogGUIHorizontalLayout(
                        new DialogGUITextInput(_hoursText, false, 6, s => _hoursText = s, 80f, 24f), new DialogGUILabel(" h ", 30f, 24f),
                        new DialogGUITextInput(_minutesText, false, 2, s => _minutesText = s, 50f, 24f), new DialogGUILabel(" m", 30f, 24f)),
                    new DialogGUIToggle(() => _mode == PimpWarpRequestMode.UntilStopped, "Warp until I stop", on => { if (on) _mode = PimpWarpRequestMode.UntilStopped; }, 360f, 24f),
                    new DialogGUIHorizontalLayout(
                        new DialogGUILabel("Kind:", 50f, 24f),
                        new DialogGUIToggle(() => !_physics, "Rails", on => { if (on) _physics = false; }, 100f, 24f),
                        new DialogGUIToggle(() => _physics, "Physics", on => { if (on) _physics = true; }, 100f, 24f)),
                    new DialogGUIButton(() => "Start at: " + RateText(RateChoices[_rateChoice]) + "  (click to change)", () => _rateChoice = (_rateChoice + 1) % RateChoices.Length, 360f, 26f, false),
                    new DialogGUILabel("During the warp your warp keys and buttons change its rate, up to the fastest everyone can use; " +
                                       GameSettings.TIME_WARP_STOP.primary + " stops it.", 360f, 40f),
                    new DialogGUILabel(() => TargetPreview(), 360f, 24f),
                    new DialogGUILabel(() => AlarmPreview(), 360f, 40f),
                    new DialogGUIHorizontalLayout(
                        new DialogGUIFlexibleSpace(),
                        new DialogGUIButton("Ask everyone", Ask, 140f, 30f, true),
                        new DialogGUIButton("Cancel", () => _request = null, 100f, 30f, true))));
            _request = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false, "");
        }

        private bool TryTarget(out double value, out string error)
        {
            value = 0; error = null;
            switch (_mode)
            {
                case PimpWarpRequestMode.ToTime:
                    if (!double.IsNaN(_exactTarget) && _dateText == _exactText) value = _exactTarget;
                    else if (DateTime.TryParseExact(_dateText, new[] { DateFormat, "yyyy-MM-dd HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d))
                        value = (d - CalendarStart).TotalSeconds;
                    else { error = "Date: use yyyy-MM-dd HH:mm:ss"; return false; }
                    if (value <= Planetarium.GetUniversalTime() + 1) { error = "That time has already passed"; return false; }
                    return true;
                case PimpWarpRequestMode.ForDuration:
                    if (!double.TryParse(_hoursText, NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) h = 0;
                    if (!double.TryParse(_minutesText, NumberStyles.Float, CultureInfo.InvariantCulture, out var m)) m = 0;
                    value = h * 3600 + m * 60;
                    if (value < 60) { error = "Warp for at least a minute"; return false; }
                    return true;
                default:
                    return true;
            }
        }

        private string TargetPreview()
        {
            if (_mode == PimpWarpRequestMode.UntilStopped) return "Until you press stop (" + GameSettings.TIME_WARP_STOP.primary + ")";
            if (!TryTarget(out var v, out var err)) return err;
            var t = _mode == PimpWarpRequestMode.ToTime ? v : Planetarium.GetUniversalTime() + v;
            return "Lands at " + Date(t) + " · in " + Span(t - Planetarium.GetUniversalTime());
        }

        // "2h 14m 20s", "3d 4h 05m": the time left, ticking down while the window and the popup are open.
        private static string Span(double s)
        {
            if (!(s > 0)) return "now";
            var t = TimeSpan.FromSeconds(Math.Floor(s));
            if (t.TotalDays >= 1) return (int)t.TotalDays + "d " + t.Hours + "h " + t.Minutes.ToString("00") + "m";
            if (t.TotalHours >= 1) return t.Hours + "h " + t.Minutes.ToString("00") + "m " + t.Seconds.ToString("00") + "s";
            return t.Minutes + "m " + t.Seconds.ToString("00") + "s";
        }

        /// <summary>
        /// W4.3 (Jacob, 26 Oct): KSP's TimeWarp.WarpTo under Consensus (TimeWarp_WarpTo): every "warp to"
        /// (the space centre's sunrise, the map's apoapsis/periapsis/manoeuvre/here, Principia's warp to
        /// manoeuvre, Kerbal Alarm Clock) opens the request window filled with that exact time instead of
        /// warping this game alone.
        /// </summary>
        internal static void RequestTo(double ut, string source)
        {
            var me = Instance;
            if (me == null) return;
            me._mode = PimpWarpRequestMode.ToTime;
            me._exactTarget = ut;
            me._source = source ?? "";
            me._exactText = me._dateText = CalendarStart.AddSeconds(ut).ToString(DateFormat, CultureInfo.InvariantCulture);
            Close(ref me._request);
            if (me._vote == null) me.OpenRequest();
        }

        private static string AlarmPreview()
        {
            var a = PimpWarpReporter.NextAlarm(out var name);
            if (double.IsNaN(a)) return "No alarm ahead (Kerbal Alarm Clock).";
            return "Stops early at the next alarm:\n  \"" + name + "\" at " + Date(a);
        }

        private void Ask()
        {
            _request = null;
            if (!TryTarget(out var value, out var err)) { ScreenMessages.PostScreenMessage("Warp not asked: " + err, 4f, ScreenMessageStyle.UPPER_CENTER); return; }
            var msg = NetworkMain.CliMsgFactory.CreateNewMessageData<WarpProposeMsgData>();
            msg.Mode = _mode;
            msg.Value = value;
            msg.Physics = _physics;
            msg.RequestedRate = RateChoices[_rateChoice];
            msg.Report.CopyFrom(PimpWarpReporter.Local(_physics));
            LunaLog.Log(LogPrefix + "Warp request sent: " + _mode + (_mode == PimpWarpRequestMode.UntilStopped ? "" : " " + F(value, "F1")) + (_physics ? ", physics" : ", rails") +
                        ", " + RateText(msg.RequestedRate) + "; this game: " + (msg.Report.CanWarp ? "can warp up to " + F(msg.Report.MaxRate, "N0") + "x" : "cannot warp: " + msg.Report.Reason) + ".");
            Send(msg);
            ScreenMessages.PostScreenMessage("Warp request sent", 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        // ----- the vote popup -----

        internal void OnAsk(int id, string proposer, PimpWarpRequestMode mode, double targetUt, bool physics, double requestedRate, double timeoutS, double alarmUt, string alarmName)
        {
            Close(ref _vote);
            _voteId = id;
            _voteDeadline = Time.realtimeSinceStartup + (float)timeoutS;
            var report = PimpWarpReporter.Local(physics);
            LunaLog.Log(LogPrefix + "Warp request " + id + " from " + proposer + ": asking (this game: " + (report.CanWarp ? "can warp up to " + F(report.MaxRate, "N0") + "x" : "cannot warp: " + report.Reason) + ").");
            var what = (mode == PimpWarpRequestMode.UntilStopped ? "until they stop" : "to " + Date(targetUt)) + " · " + (physics ? "physics" : "rails") +
                       (requestedRate > 1 ? " · " + F(requestedRate, "N0") + "x" : "");
            var alarm = double.IsNaN(alarmUt) ? "" : "\n(stops at the earliest alarm: \"" + alarmName + "\" at " + Date(alarmUt) + ")";
            var me = report.CanWarp ? "Your game: can warp, up to " + F(report.MaxRate, "N0") + "x" : "Your game: cannot warp: " + report.Reason;
            var dialog = new MultiOptionDialog("PimpWarpAsk", "", "Time warp request", HighLogic.UISkin, 360f,
                new DialogGUIVerticalLayout(
                    new DialogGUILabel(proposer + " has asked to time warp\n" + what + alarm, 340f, 60f),
                    new DialogGUILabel(me, 340f, 24f),
                    new DialogGUILabel(() => double.IsNaN(targetUt) ? "" : "Lands in " + Span(targetUt - Planetarium.GetUniversalTime()), 340f, 24f),
                    new DialogGUILabel(() => "No answer in " + F(Math.Max(0, _voteDeadline - Time.realtimeSinceStartup)) + " s counts as no.", 340f, 24f),
                    new DialogGUIHorizontalLayout(
                        new DialogGUIFlexibleSpace(),
                        new DialogGUIButton("Yes", () => Vote(true, physics), () => report.CanWarp, 100f, 30f, true, HighLogic.UISkin.button),
                        new DialogGUIButton("No", () => Vote(false, physics), 100f, 30f, true))));
            _vote = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false, "");
        }

        private void Vote(bool yes, bool physics)
        {
            _vote = null;
            var msg = NetworkMain.CliMsgFactory.CreateNewMessageData<WarpVoteMsgData>();
            msg.ProposalId = _voteId;
            msg.Yes = yes;
            msg.Report.CopyFrom(PimpWarpReporter.Local(physics));
            LunaLog.Log(LogPrefix + "Warp request " + _voteId + ": answered " + (yes ? "yes" : "no") + ".");
            Send(msg);
        }

        internal void OnResult(int id, bool started, string text)
        {
            if (id == _voteId || id == 0) Close(ref _vote);
            LunaLog.Log(LogPrefix + "Warp request " + id + ": " + text);
            ScreenMessages.PostScreenMessage(text, started ? 4f : 6f, ScreenMessageStyle.UPPER_CENTER);
        }

        internal static PimpConsensusUi Instance;
        public void Start() => Instance = this;
    }
}
