using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.Warp
{
    // PiMP consensus warp (plan W4.3, 26 Oct 2026): propose, ask, vote, result, report and stop.
    // Only an explicit yes counts (Jacob, 25 Oct); a game that cannot take part blocks the warp; the
    // earliest alarm any game reports is the warp's target.

    /// <summary>What a game reports with its proposal, its vote and, during a warp, every few seconds.</summary>
    public class PimpWarpReport
    {
        /// <summary>Whether this game can take part in the requested kind of warp now.</summary>
        public bool CanWarp;
        /// <summary>Why not, in words (empty when it can).</summary>
        public string Reason = string.Empty;
        /// <summary>The highest rate this game can use now for the requested kind.</summary>
        public double MaxRate;
        /// <summary>This game's next alarm (universe time), NaN when none.</summary>
        public double NextAlarmUt = double.NaN;
        public string AlarmName = string.Empty;

        public void Serialize(NetOutgoingMessage m)
        {
            m.Write(CanWarp);
            m.Write(Reason ?? string.Empty);
            m.Write(MaxRate);
            m.Write(NextAlarmUt);
            m.Write(AlarmName ?? string.Empty);
        }

        public void Deserialize(NetIncomingMessage m)
        {
            CanWarp = m.ReadBoolean();
            Reason = m.ReadString();
            MaxRate = m.ReadDouble();
            NextAlarmUt = m.ReadDouble();
            AlarmName = m.ReadString();
        }

        public int GetByteCount() => 1 + (Reason ?? string.Empty).GetByteCount() + 2 * sizeof(double) + (AlarmName ?? string.Empty).GetByteCount();

        public void CopyFrom(PimpWarpReport r)
        {
            CanWarp = r.CanWarp; Reason = r.Reason; MaxRate = r.MaxRate; NextAlarmUt = r.NextAlarmUt; AlarmName = r.AlarmName;
        }
    }

    /// <summary>Request kinds (Jacob: all three).</summary>
    public enum PimpWarpRequestMode { UntilStopped = 0, ToTime = 1, ForDuration = 2 }

    /// <summary>Client to server: a player asks everyone to warp.</summary>
    public class WarpProposeMsgData : WarpBaseMsgData
    {
        internal WarpProposeMsgData() { }
        public override WarpMessageType WarpMessageType => WarpMessageType.Propose;
        public override string ClassName { get; } = nameof(WarpProposeMsgData);

        public PimpWarpRequestMode Mode;
        /// <summary>ToTime: the universe time; ForDuration: game seconds; UntilStopped: ignored.</summary>
        public double Value;
        /// <summary>false = rails, true = physics.</summary>
        public bool Physics;
        /// <summary>0 = the highest everyone can use.</summary>
        public double RequestedRate;
        public PimpWarpReport Report = new PimpWarpReport();

        internal override void InternalSerialize(NetOutgoingMessage m)
        {
            base.InternalSerialize(m);
            m.Write((int)Mode); m.Write(Value); m.Write(Physics); m.Write(RequestedRate); Report.Serialize(m);
        }

        internal override void InternalDeserialize(NetIncomingMessage m)
        {
            base.InternalDeserialize(m);
            Mode = (PimpWarpRequestMode)m.ReadInt32(); Value = m.ReadDouble(); Physics = m.ReadBoolean(); RequestedRate = m.ReadDouble(); Report.Deserialize(m);
        }

        internal override int InternalGetMessageSize() => base.InternalGetMessageSize() + sizeof(int) + sizeof(double) + 1 + sizeof(double) + Report.GetByteCount();
    }

    /// <summary>Server to client: the popup. Answer with <see cref="WarpVoteMsgData"/> before the deadline.</summary>
    public class WarpAskMsgData : WarpBaseMsgData
    {
        internal WarpAskMsgData() { }
        public override WarpMessageType WarpMessageType => WarpMessageType.Ask;
        public override string ClassName { get; } = nameof(WarpAskMsgData);

        public int ProposalId;
        public string Proposer = string.Empty;
        public PimpWarpRequestMode Mode;
        /// <summary>The universe time the warp would land on (NaN until stopped).</summary>
        public double TargetUt;
        public bool Physics;
        public double RequestedRate;
        /// <summary>Seconds left to answer when sent.</summary>
        public double TimeoutS;
        /// <summary>The earliest alarm known so far (the proposer's), NaN when none.</summary>
        public double AlarmUt;
        public string AlarmName = string.Empty;

        internal override void InternalSerialize(NetOutgoingMessage m)
        {
            base.InternalSerialize(m);
            m.Write(ProposalId); m.Write(Proposer ?? string.Empty); m.Write((int)Mode); m.Write(TargetUt); m.Write(Physics); m.Write(RequestedRate);
            m.Write(TimeoutS); m.Write(AlarmUt); m.Write(AlarmName ?? string.Empty);
        }

        internal override void InternalDeserialize(NetIncomingMessage m)
        {
            base.InternalDeserialize(m);
            ProposalId = m.ReadInt32(); Proposer = m.ReadString(); Mode = (PimpWarpRequestMode)m.ReadInt32(); TargetUt = m.ReadDouble(); Physics = m.ReadBoolean();
            RequestedRate = m.ReadDouble(); TimeoutS = m.ReadDouble(); AlarmUt = m.ReadDouble(); AlarmName = m.ReadString();
        }

        internal override int InternalGetMessageSize() => base.InternalGetMessageSize() + sizeof(int) + (Proposer ?? string.Empty).GetByteCount() + sizeof(int) +
                                                           sizeof(double) + 1 + 3 * sizeof(double) + (AlarmName ?? string.Empty).GetByteCount();
    }

    /// <summary>Client to server: a yes or no, with this game's report.</summary>
    public class WarpVoteMsgData : WarpBaseMsgData
    {
        internal WarpVoteMsgData() { }
        public override WarpMessageType WarpMessageType => WarpMessageType.Vote;
        public override string ClassName { get; } = nameof(WarpVoteMsgData);

        public int ProposalId;
        public bool Yes;
        public PimpWarpReport Report = new PimpWarpReport();

        internal override void InternalSerialize(NetOutgoingMessage m)
        {
            base.InternalSerialize(m);
            m.Write(ProposalId); m.Write(Yes); Report.Serialize(m);
        }

        internal override void InternalDeserialize(NetIncomingMessage m)
        {
            base.InternalDeserialize(m);
            ProposalId = m.ReadInt32(); Yes = m.ReadBoolean(); Report.Deserialize(m);
        }

        internal override int InternalGetMessageSize() => base.InternalGetMessageSize() + sizeof(int) + 1 + Report.GetByteCount();
    }

    /// <summary>Server to client: how a proposal ended (started, or why not).</summary>
    public class WarpResultMsgData : WarpBaseMsgData
    {
        internal WarpResultMsgData() { }
        public override WarpMessageType WarpMessageType => WarpMessageType.Result;
        public override string ClassName { get; } = nameof(WarpResultMsgData);

        public int ProposalId;
        public bool Started;
        public string Text = string.Empty;

        internal override void InternalSerialize(NetOutgoingMessage m)
        {
            base.InternalSerialize(m);
            m.Write(ProposalId); m.Write(Started); m.Write(Text ?? string.Empty);
        }

        internal override void InternalDeserialize(NetIncomingMessage m)
        {
            base.InternalDeserialize(m);
            ProposalId = m.ReadInt32(); Started = m.ReadBoolean(); Text = m.ReadString();
        }

        internal override int InternalGetMessageSize() => base.InternalGetMessageSize() + sizeof(int) + 1 + (Text ?? string.Empty).GetByteCount();
    }

    /// <summary>Client to server, during a warp: this game's report (rate limit, alarm).</summary>
    public class WarpReportMsgData : WarpBaseMsgData
    {
        internal WarpReportMsgData() { }
        public override WarpMessageType WarpMessageType => WarpMessageType.Report;
        public override string ClassName { get; } = nameof(WarpReportMsgData);

        public PimpWarpReport Report = new PimpWarpReport();

        internal override void InternalSerialize(NetOutgoingMessage m) { base.InternalSerialize(m); Report.Serialize(m); }
        internal override void InternalDeserialize(NetIncomingMessage m) { base.InternalDeserialize(m); Report.Deserialize(m); }
        internal override int InternalGetMessageSize() => base.InternalGetMessageSize() + Report.GetByteCount();
    }

    /// <summary>Client to server: the proposer asks to stop the warp.</summary>
    public class WarpStopRequestMsgData : WarpBaseMsgData
    {
        internal WarpStopRequestMsgData() { }
        public override WarpMessageType WarpMessageType => WarpMessageType.StopRequest;
        public override string ClassName { get; } = nameof(WarpStopRequestMsgData);
    }

    /// <summary>Client to server, during a group warp: the proposer asks for a new rate (clamped to the limit).</summary>
    public class WarpRateRequestMsgData : WarpBaseMsgData
    {
        internal WarpRateRequestMsgData() { }
        public override WarpMessageType WarpMessageType => WarpMessageType.RateRequest;
        public override string ClassName { get; } = nameof(WarpRateRequestMsgData);

        public double Rate;

        internal override void InternalSerialize(NetOutgoingMessage m) { base.InternalSerialize(m); m.Write(Rate); }
        internal override void InternalDeserialize(NetIncomingMessage m) { base.InternalDeserialize(m); Rate = m.ReadDouble(); }
        internal override int InternalGetMessageSize() => base.InternalGetMessageSize() + sizeof(double);
    }
}
