using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Server;
using Server.Settings.Structures;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Server.Pimp
{
    /// <summary>
    /// PiMP consensus warp (plan W4.3, 25 Oct 2026): propose and vote. Jacob's rules (25 Oct):
    ///   - a request is "until I stop", "to a time" or "for a duration", rails or physics;
    ///   - every other player gets the popup; only an explicit yes counts: a no, no answer within
    ///     ConsensusVoteTimeoutS, or a game that cannot take part (with its reason) means no warp;
    ///   - the warp runs at the lowest rate every game can use (or the proposer's, if lower);
    ///   - only the proposer or an alarm stops it: the earliest alarm any game reports is the target;
    ///   - a player alone warps at once, unless ConsensusAskWhenAlone (testing) shows them the popup.
    /// During the warp each game reports every few seconds; a lower rate limit or an earlier alarm
    /// moves the clock on from now (Rebase). One proposal at a time.
    /// </summary>
    public static partial class PimpConsensusWarp
    {
        private const double MaxPhysicsRate = 4;
        private const double DefaultStartRate = 1000;
        private static double Cap => Math.Max(2, WarpSettings.SettingsStore.ConsensusMaxRate);

        private sealed class Proposal
        {
            public int Id;
            public ClientStructure Proposer;
            public PimpWarpRequestMode Mode;
            public bool Physics;
            public double RequestedRate;
            public double TargetUt;
            public long DeadlineTicks;
            public readonly Dictionary<ClientStructure, PimpWarpReport> Yes = new Dictionary<ClientStructure, PimpWarpReport>();
            public readonly HashSet<ClientStructure> Asked = new HashSet<ClientStructure>();
        }

        private static Proposal _proposal;
        private static int _nextProposalId = 1;
        private static Timer _voteTimer;
        private static ClientStructure _warpProposer;
        private static bool _warpPhysics;
        private static readonly Dictionary<ClientStructure, PimpWarpReport> Reports = new Dictionary<ClientStructure, PimpWarpReport>();

        private static string T(double ut) => ut.ToString("F1", CultureInfo.InvariantCulture);
        private static PimpWarpReport Copy(PimpWarpReport r) { var c = new PimpWarpReport(); c.CopyFrom(r); return c; }

        public static void HandlePropose(ClientStructure client, WarpProposeMsgData msg)
        {
            lock (Gate)
            {
                if (!Enabled) { Result(new[] { client }, 0, false, "Time warp on this server is not a group warp."); return; }
                if (_proposal != null) { Result(new[] { client }, 0, false, $"Warp not asked: {_proposal.Proposer.PlayerName}'s request is being decided."); return; }
                if (WarpRunning) { Result(new[] { client }, 0, false, "Warp not asked: a group warp is running."); return; }
                if (!msg.Report.CanWarp) { Result(new[] { client }, 0, false, "Warp not asked: your game cannot warp: " + msg.Report.Reason); return; }

                var u = UniverseNow;
                var target = msg.Mode == PimpWarpRequestMode.ToTime ? msg.Value
                           : msg.Mode == PimpWarpRequestMode.ForDuration ? u + msg.Value
                           : double.NaN;
                if (!double.IsNaN(target) && !(target > u + 1)) { Result(new[] { client }, 0, false, "Warp not asked: that time has already passed."); return; }

                var p = new Proposal
                {
                    Id = _nextProposalId++,
                    Proposer = client,
                    Mode = msg.Mode,
                    Physics = msg.Physics,
                    RequestedRate = msg.RequestedRate,
                    TargetUt = target,
                };
                p.Yes[client] = Copy(msg.Report);

                var others = ClientRetriever.GetAuthenticatedClients().Where(c => !ReferenceEquals(c, client)).ToList();
                var askAlone = others.Count == 0 && WarpSettings.SettingsStore.ConsensusAskWhenAlone;
                LunaLog.Normal($"[PiMP] Warp request {p.Id} from {client.PlayerName}: {Describe(p)}; asking {(askAlone ? "the proposer (ConsensusAskWhenAlone)" : others.Count + " other player(s)")}");
                _proposal = p;
                if (others.Count == 0 && !askAlone)
                {
                    Decide(p);
                    return;
                }
                if (askAlone) { p.Yes.Remove(client); p.Asked.Add(client); }
                foreach (var c in others) p.Asked.Add(c);

                var timeout = Math.Max(5, WarpSettings.SettingsStore.ConsensusVoteTimeoutS);
                p.DeadlineTicks = NowTicks + TimeSpan.FromSeconds(timeout).Ticks;
                var alarm = EarliestAlarm(p.Yes, u, out var alarmName);
                foreach (var c in p.Asked)
                {
                    var ask = ServerContext.ServerMessageFactory.CreateNewMessageData<WarpAskMsgData>();
                    ask.ProposalId = p.Id;
                    ask.Proposer = client.PlayerName;
                    ask.Mode = p.Mode;
                    ask.TargetUt = p.TargetUt;
                    ask.Physics = p.Physics;
                    ask.RequestedRate = p.RequestedRate;
                    ask.TimeoutS = timeout;
                    ask.AlarmUt = alarm;
                    ask.AlarmName = alarmName;
                    MessageQueuer.SendToClient<WarpSrvMsg>(c, ask);
                }
                _voteTimer?.Dispose();
                _voteTimer = new Timer(_ => CheckDeadline(), null, 500, 500);
            }
        }

        public static void HandleVote(ClientStructure client, WarpVoteMsgData msg)
        {
            lock (Gate)
            {
                var p = _proposal;
                if (p == null || msg.ProposalId != p.Id || !p.Asked.Contains(client)) return;
                if (!msg.Yes) { Fail(p, $"{client.PlayerName} said no."); return; }
                if (!msg.Report.CanWarp) { Fail(p, $"{client.PlayerName}: {msg.Report.Reason}."); return; }
                p.Asked.Remove(client);
                p.Yes[client] = Copy(msg.Report);
                LunaLog.Normal($"[PiMP] Warp request {p.Id}: {client.PlayerName} said yes (up to {msg.Report.MaxRate:G6}x)");
                if (p.Asked.Count == 0) Decide(p);
            }
        }

        public static void HandleReport(ClientStructure client, PimpWarpReport report)
        {
            lock (Gate)
            {
                if (!WarpRunning) return;
                Reports[client] = Copy(report);
                var c = _clock;
                var u = UniverseNow;
                var limit = GroupLimit(out var limitedBy);
                if (limit < c.Rate)
                {
                    if (limit <= 1)
                    {
                        LunaLog.Normal($"[PiMP] Group warp: {limitedBy} cannot warp now ({report.Reason}); stopping");
                        Stop();
                    }
                    else Rebase(limit, c.Target, $"rate lowered for {limitedBy} ({limit:G6}x)", limit);
                    c = _clock;
                }
                else if (Math.Abs(limit - c.Limit) > 0.01 * limit && WarpRunning) { Rebase(c.Rate, c.Target, $"limit now {limit:G6}x ({limitedBy})", limit); c = _clock; }
                if (!double.IsNaN(report.NextAlarmUt) && report.NextAlarmUt > u + 1 && (double.IsNaN(c.Target) || report.NextAlarmUt < c.Target - 0.5) && WarpRunning)
                    Rebase(c.Rate, report.NextAlarmUt, $"target moved to {client.PlayerName}'s alarm \"{report.AlarmName}\"");
            }
        }

        /// <summary>W4.3 (Jacob, 25 Oct): the proposer sets the rate while the warp runs, up to the group limit.</summary>
        public static void HandleRateRequest(ClientStructure client, double rate)
        {
            lock (Gate)
            {
                if (!WarpRunning) return;
                if (!ReferenceEquals(client, _warpProposer))
                {
                    Result(new[] { client }, 0, false, $"Only {_warpProposer?.PlayerName ?? "the player who asked"} can change this warp's rate.");
                    return;
                }
                var c = _clock;
                var limit = GroupLimit(out _);
                var r = Math.Max(1.0000001, Math.Min(rate, limit));
                if (!(r > 1)) return;
                if (Math.Abs(r - c.Rate) < 1e-6 * r) return;
                Rebase(r, c.Target, $"{client.PlayerName} set {r:G6}x", limit);
            }
        }

        // The fastest rate every game in the warp can use now, capped by the server; who limits it.
        private static double GroupLimit(out string limitedBy)
        {
            var limit = Cap;
            limitedBy = "the server's limit";
            foreach (var kv in Reports)
            {
                var m = kv.Value.CanWarp ? kv.Value.MaxRate : 1;
                if (m < limit) { limit = m; limitedBy = kv.Key.PlayerName; }
            }
            if (_warpPhysics && limit > MaxPhysicsRate) { limit = MaxPhysicsRate; limitedBy = "physics warp"; }
            return limit;
        }

        public static void HandleStopRequest(ClientStructure client)
        {
            lock (Gate)
            {
                if (!WarpRunning) return;
                if (!ReferenceEquals(client, _warpProposer))
                {
                    Result(new[] { client }, 0, false, $"Only {_warpProposer?.PlayerName ?? "the player who asked"} (or an alarm) can stop this warp.");
                    return;
                }
                LunaLog.Normal($"[PiMP] Group warp: {client.PlayerName} asked to stop: {Stop()}");
            }
        }

        public static void OnDisconnect(ClientStructure client)
        {
            lock (Gate)
            {
                Reports.Remove(client);
                var p = _proposal;
                if (p != null)
                {
                    if (ReferenceEquals(p.Proposer, client)) Fail(p, $"{client.PlayerName} left.");
                    else if (p.Asked.Remove(client) && p.Asked.Count == 0) Decide(p);
                    else p.Yes.Remove(client);
                }
                if (WarpRunning && ReferenceEquals(client, _warpProposer))
                    LunaLog.Normal($"[PiMP] Group warp: {client.PlayerName} left; {Stop()}");
            }
        }

        // Everyone said yes: start at the lowest rate every game can use, landing on the earliest of the
        // request's target and every game's next alarm.
        private static void Decide(Proposal p)
        {
            var u = UniverseNow;
            var limit = double.PositiveInfinity;
            var limitedBy = "";
            foreach (var kv in p.Yes)
            {
                if (kv.Value.MaxRate < limit) { limit = kv.Value.MaxRate; limitedBy = kv.Key.PlayerName; }
            }
            if (p.Physics) limit = Math.Min(limit, MaxPhysicsRate);
            limit = Math.Min(limit, Cap);
            // Jacob (25 Oct): the request picks a start rate; the proposer changes it during the warp, up to the limit.
            var rate = Math.Min(p.RequestedRate > 1 ? p.RequestedRate : DefaultStartRate, limit);
            if (!(rate > 1) || double.IsInfinity(rate)) { Fail(p, $"no warp rate everyone can use (limited by {limitedBy})."); return; }
            var alarm = EarliestAlarm(p.Yes, u, out var alarmName);
            var target = p.TargetUt;
            if (!double.IsNaN(target) && !(target > u + 1)) { Fail(p, "that time has already passed."); return; }
            var why = "";
            if (!double.IsNaN(alarm) && (double.IsNaN(target) || alarm < target)) { target = alarm; why = $", stopping at the alarm \"{alarmName}\""; }

            ClearProposal();
            var started = StartAt(rate, target, p.Physics, limit, p.Proposer.PlayerName);
            if (!started.StartsWith("started")) { Result(Everyone(), p.Id, false, "Warp not started: " + started); return; }
            _warpProposer = p.Proposer;
            _warpPhysics = p.Physics;
            Reports.Clear();
            foreach (var kv in p.Yes) Reports[kv.Key] = kv.Value;
            var text = $"Group warp {rate:N0}x asked by {p.Proposer.PlayerName}, up to {limit:N0}x" +
                       (limit < Cap ? $" (limited by {limitedBy})" : " (the server's limit)") + why + ".";
            LunaLog.Normal($"[PiMP] Warp request {p.Id}: {text}");
            Result(Everyone(), p.Id, true, text);
        }

        private static void Fail(Proposal p, string why)
        {
            ClearProposal();
            LunaLog.Normal($"[PiMP] Warp request {p.Id} from {p.Proposer.PlayerName}: not started: {why}");
            Result(Everyone(), p.Id, false, "Warp not started: " + why);
        }

        private static void CheckDeadline()
        {
            lock (Gate)
            {
                var p = _proposal;
                if (p == null) { _voteTimer?.Dispose(); _voteTimer = null; return; }
                if (NowTicks < p.DeadlineTicks) return;
                Fail(p, string.Join(", ", p.Asked.Select(c => c.PlayerName)) + " did not answer.");
            }
        }

        private static void ClearProposal()
        {
            _proposal = null;
            _voteTimer?.Dispose();
            _voteTimer = null;
        }

        private static double EarliestAlarm(Dictionary<ClientStructure, PimpWarpReport> reports, double u, out string name)
        {
            var best = double.NaN;
            name = "";
            foreach (var kv in reports)
            {
                var a = kv.Value.NextAlarmUt;
                if (double.IsNaN(a) || !(a > u + 1)) continue;
                if (double.IsNaN(best) || a < best) { best = a; name = $"{kv.Value.AlarmName} ({kv.Key.PlayerName})"; }
            }
            return best;
        }

        private static string Describe(Proposal p) =>
            (p.Mode == PimpWarpRequestMode.UntilStopped ? "until stopped" : "to t=" + T(p.TargetUt)) + (p.Physics ? ", physics" : ", rails") +
            (p.RequestedRate > 1 ? $", at most {p.RequestedRate:G6}x" : ", highest everyone can use");

        private static IEnumerable<ClientStructure> Everyone() => ClientRetriever.GetAuthenticatedClients();

        private static void Result(IEnumerable<ClientStructure> to, int proposalId, bool started, string text)
        {
            foreach (var c in to)
            {
                var r = ServerContext.ServerMessageFactory.CreateNewMessageData<WarpResultMsgData>();
                r.ProposalId = proposalId;
                r.Started = started;
                r.Text = text;
                MessageQueuer.SendToClient<WarpSrvMsg>(c, r);
            }
        }
    }
}
