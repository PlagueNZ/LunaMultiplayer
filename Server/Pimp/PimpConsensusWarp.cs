using LmpCommon.Enums;
using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Server;
using LmpCommon.Pimp;
using LmpCommon.Time;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Server;
using Server.Settings.Structures;
using System.Globalization;
using System.Threading;

namespace Server.Pimp
{
    /// <summary>
    /// PiMP consensus warp (plan W4.1, 25 Oct 2026): the server's warp clock. In WarpMode.Consensus
    /// everyone stays in the latest subspace, and a warp moves that subspace's offset at the warp's
    /// rate (<see cref="PimpWarpClock"/>), so WarpContext.UniverseTime, the bridge's Principia and every
    /// game follow one clock. W4.1 starts and stops a warp from the server console only
    /// (/warpclock); proposals and votes come in W4.3, games following the clock in W4.2.
    /// Stopping never lands anyone past a time: a stop becomes a target a little ahead (2 real
    /// seconds at the warp's rate), which the clock lands on exactly.
    /// </summary>
    public static class PimpConsensusWarp
    {
        private static readonly object Gate = new object();
        private static volatile PimpWarpClock _clock;
        private static Timer _timer;

        /// <summary>Real seconds of warp a stop leaves, so games can land on the target.</summary>
        public const double StopLeadS = 2;

        public static long StartTicks => TimeContext.StartTime.Ticks;
        public static long NowTicks => LunaNetworkTime.UtcNow.Ticks;

        /// <summary>The current clock, or null when no consensus warp has run since the start.</summary>
        public static PimpWarpClock Clock => _clock;

        public static bool Enabled => WarpSettings.SettingsStore.WarpMode == WarpMode.Consensus;

        /// <summary>The offset of <paramref name="subspace"/> now, if the clock drives it.</summary>
        public static bool TryOffset(Subspace subspace, out double offset)
        {
            var c = _clock;
            if (c != null && subspace != null && c.SubspaceKey == subspace.Id)
            {
                offset = c.OffsetAt(NowTicks, StartTicks);
                return true;
            }
            offset = 0;
            return false;
        }

        /// <summary>Starts a warp at <paramref name="rate"/>, open or landing <paramref name="secondsAhead"/> of game time from now.</summary>
        public static string Start(double rate, double secondsAhead)
        {
            lock (Gate)
            {
                if (!Enabled) return "not started: WarpMode is not Consensus";
                if (!(rate > 1)) return "not started: rate must be above 1";
                if (_clock != null && _clock.IsWarping && !_clock.HasLanded(NowTicks, StartTicks)) return "not started: a warp is running (" + _clock + ")";
                var sub = WarpContext.LatestSubspace;
                var now = NowTicks;
                var offset = WarpContext.LatestSubspaceTime;   // includes a landed clock's offset
                sub.Time = offset;
                var u = (now - StartTicks) / 1e7 + offset;
                var target = double.IsNaN(secondsAhead) ? double.NaN : u + secondsAhead;
                Set(new PimpWarpClock(sub.Id, now, offset, rate, target), "started at t=" + u.ToString("F1", CultureInfo.InvariantCulture));
                return "started: " + _clock;
            }
        }

        /// <summary>Stops the warp: it lands StopLeadS real seconds ahead (or sooner, if its target is nearer).</summary>
        public static string Stop()
        {
            lock (Gate)
            {
                var c = _clock;
                var now = NowTicks;
                if (c == null || !c.IsWarping || c.HasLanded(now, StartTicks)) return "no warp running";
                var u = c.UniverseTime(now, StartTicks);
                var target = u + c.Rate * StopLeadS;
                if (!double.IsNaN(c.Target) && c.Target < target) return "already landing at t=" + c.Target.ToString("F1", CultureInfo.InvariantCulture);
                Set(new PimpWarpClock(c.SubspaceKey, now, c.OffsetAt(now, StartTicks), c.Rate, target), "stopping");
                return "stopping: " + _clock;
            }
        }

        public static string Status()
        {
            var c = _clock;
            if (c == null) return "no warp clock (WarpMode " + WarpSettings.SettingsStore.WarpMode + ")";
            var now = NowTicks;
            return c + (c.IsWarping && !c.HasLanded(now, StartTicks) ? "; warping" : "; at rest") +
                   ", universe t=" + c.UniverseTime(now, StartTicks).ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>Sends the current clock to one game (after its subspace list).</summary>
        public static void SendTo(ClientStructure client)
        {
            var c = _clock;
            if (c == null || client == null) return;
            var msg = ServerContext.ServerMessageFactory.CreateNewMessageData<WarpClockMsgData>();
            msg.From(c);
            MessageQueuer.SendToClient<WarpSrvMsg>(client, msg);
        }

        private static void Set(PimpWarpClock c, string why)
        {
            _clock = c;
            LunaLog.Normal("[PiMP] Warp clock " + why + ": " + c);
            var msg = ServerContext.ServerMessageFactory.CreateNewMessageData<WarpClockMsgData>();
            msg.From(c);
            MessageQueuer.SendToAllClients<WarpSrvMsg>(msg);
            if (c.IsWarping && _timer == null) _timer = new Timer(_ => Check(), null, 100, 100);
        }

        // Every 100 ms while a warp runs: once it has landed, freeze the subspace's offset there and
        // tell everyone the clock is at rest.
        private static void Check()
        {
            lock (Gate)
            {
                var c = _clock;
                var now = NowTicks;
                if (c == null || !c.IsWarping) { StopTimer(); return; }
                if (!c.HasLanded(now, StartTicks)) return;
                var offset = c.OffsetAt(now, StartTicks);
                if (WarpContext.Subspaces.TryGetValue(c.SubspaceKey, out var sub)) sub.Time = offset;
                StopTimer();
                Set(PimpWarpClock.AtRest(c.SubspaceKey, now, offset), "landed at t=" + c.Target.ToString("F1", CultureInfo.InvariantCulture));
            }
        }

        private static void StopTimer()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
