using Lidgren.Network;
using LmpCommon.Message;
using LmpCommon.Message.Base;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LmpCommonTest
{
    // PiMP consensus warp W4.3 (26 Oct 2026): the vote messages survive the wire.
    [TestClass]
    public class PimpConsensusMsgTest
    {
        private static readonly NetClient Net = new NetClient(new NetPeerConfiguration("TESTS"));

        private static IMessageData RoundTrip(IMessageBase msg, FactoryBase factory)
        {
            var send = Net.CreateMessage(msg.GetMessageSize());
            msg.Serialize(send);
            Assert.IsTrue(msg.GetMessageSize() >= send.LengthBytes);
            var bytes = send.ReadBytes(send.LengthBytes);
            var recv = Net.CreateIncomingMessage(NetIncomingMessageType.Data, bytes);
            recv.LengthBytes = send.LengthBytes;
            return factory.Deserialize(recv, Environment.TickCount).Data;
        }

        [TestMethod]
        public void TestProposeAndVoteRoundTrip()
        {
            var cli = new ClientMessageFactory();
            var p = cli.CreateNewMessageData<WarpProposeMsgData>();
            p.Mode = PimpWarpRequestMode.ForDuration; p.Value = 21600; p.Physics = true; p.RequestedRate = 1000;
            p.Report.CanWarp = false; p.Report.Reason = "in atmosphere"; p.Report.MaxRate = 1; p.Report.NextAlarmUt = 845000.5; p.Report.AlarmName = "Moon burn";
            var r = (WarpProposeMsgData)RoundTrip(cli.CreateNew<WarpCliMsg>(p), cli);
            Assert.AreEqual(PimpWarpRequestMode.ForDuration, r.Mode); Assert.AreEqual(21600, r.Value); Assert.IsTrue(r.Physics); Assert.AreEqual(1000, r.RequestedRate);
            Assert.IsFalse(r.Report.CanWarp); Assert.AreEqual("in atmosphere", r.Report.Reason); Assert.AreEqual(845000.5, r.Report.NextAlarmUt); Assert.AreEqual("Moon burn", r.Report.AlarmName);

            var v = cli.CreateNewMessageData<WarpVoteMsgData>();
            v.ProposalId = 7; v.Yes = true; v.Report.CanWarp = true; v.Report.MaxRate = 100000; v.Report.NextAlarmUt = double.NaN;
            var rv = (WarpVoteMsgData)RoundTrip(cli.CreateNew<WarpCliMsg>(v), cli);
            Assert.AreEqual(7, rv.ProposalId); Assert.IsTrue(rv.Yes); Assert.AreEqual(100000, rv.Report.MaxRate); Assert.IsTrue(double.IsNaN(rv.Report.NextAlarmUt));
        }

        [TestMethod]
        public void TestAskAndResultRoundTrip()
        {
            var srv = new ServerMessageFactory();
            var a = srv.CreateNewMessageData<WarpAskMsgData>();
            a.ProposalId = 3; a.Proposer = "Plague"; a.Mode = PimpWarpRequestMode.UntilStopped; a.TargetUt = double.NaN; a.RequestedRate = 0; a.TimeoutS = 30; a.AlarmUt = 900000; a.AlarmName = "x (Bob)";
            var r = (WarpAskMsgData)RoundTrip(srv.CreateNew<WarpSrvMsg>(a), srv);
            Assert.AreEqual(3, r.ProposalId); Assert.AreEqual("Plague", r.Proposer); Assert.IsTrue(double.IsNaN(r.TargetUt)); Assert.AreEqual(30, r.TimeoutS); Assert.AreEqual("x (Bob)", r.AlarmName);

            var res = srv.CreateNewMessageData<WarpResultMsgData>();
            res.ProposalId = 3; res.Started = false; res.Text = "Warp not started: Bob said no.";
            var rr = (WarpResultMsgData)RoundTrip(srv.CreateNew<WarpSrvMsg>(res), srv);
            Assert.IsFalse(rr.Started); Assert.AreEqual("Warp not started: Bob said no.", rr.Text);
        }
    }
}
