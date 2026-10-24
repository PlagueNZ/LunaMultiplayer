using Lidgren.Network;
using LmpCommon.Message;
using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LmpCommonTest
{
    // PiMP warp plan W3 (25 Oct 2026): the end-of-warp states message survives the wire.
    [TestClass]
    public class PimpWarpStatesSerializationTest
    {
        private static readonly ServerMessageFactory Factory = new ServerMessageFactory();
        private static readonly NetClient Client = new NetClient(new NetPeerConfiguration("TESTS"));

        private static WarpAuthoritativeStatesMsgData RoundTrip(WarpAuthoritativeStatesMsgData msgData)
        {
            var msg = Factory.CreateNew<WarpSrvMsg>(msgData);
            var expected = msg.GetMessageSize();
            var send = Client.CreateMessage(expected);
            msg.Serialize(send);
            Assert.IsTrue(expected >= send.LengthBytes, $"size {expected} < {send.LengthBytes}");
            var bytes = send.ReadBytes(send.LengthBytes);
            var recv = Client.CreateIncomingMessage(NetIncomingMessageType.Data, bytes);
            recv.LengthBytes = send.LengthBytes;
            var back = Factory.Deserialize(recv, Environment.TickCount);
            Assert.IsInstanceOfType(back.Data, typeof(WarpAuthoritativeStatesMsgData));
            return (WarpAuthoritativeStatesMsgData)back.Data;
        }

        [TestMethod]
        public void TestAuthoritativeStatesRoundTrip()
        {
            var d = Factory.CreateNewMessageData<WarpAuthoritativeStatesMsgData>();
            d.SubspaceKey = 5;
            d.Ut = 581348.4901477;
            d.StateCount = 2;
            d.States = new PimpVesselState[2];
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
            for (var i = 0; i < 2; i++)
            {
                d.States[i] = new PimpVesselState { VesselId = ids[i] };
                for (var k = 0; k < 8; k++) d.States[i].Orbit[k] = (i + 1) * 1000.0 + k + 0.123456789012345;
            }
            d.FrameCount = 7;
            d.Frame = new[] { 581348.49, -4.1e8, 3.3e7, 1.2e7, 1.4e11, -5.0e10, -2.2e10 };

            var r = RoundTrip(d);
            Assert.AreEqual(5, r.SubspaceKey);
            Assert.AreEqual(581348.4901477, r.Ut);
            Assert.AreEqual(2, r.StateCount);
            for (var i = 0; i < 2; i++)
            {
                Assert.AreEqual(ids[i], r.States[i].VesselId);
                for (var k = 0; k < 8; k++) Assert.AreEqual((i + 1) * 1000.0 + k + 0.123456789012345, r.States[i].Orbit[k]);
            }
            Assert.AreEqual(7, r.FrameCount);
            CollectionAssert.AreEqual(d.Frame, r.Frame);
        }

        [TestMethod]
        public void TestAuthoritativeStatesEmpty()
        {
            var d = Factory.CreateNewMessageData<WarpAuthoritativeStatesMsgData>();
            d.SubspaceKey = 1;
            d.Ut = 10;
            d.StateCount = 0;
            d.FrameCount = 0;
            var r = RoundTrip(d);
            Assert.AreEqual(0, r.StateCount);
            Assert.AreEqual(0, r.FrameCount);
        }
    }
}
