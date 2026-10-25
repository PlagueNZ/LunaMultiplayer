using LmpCommon.Message;
using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Server;
using LmpCommon.Pimp;
using Lidgren.Network;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LmpCommonTest
{
    // PiMP consensus warp W4.1 (25 Oct 2026): the warp clock's arithmetic and its message.
    [TestClass]
    public class PimpWarpClockTest
    {
        private const long S = TimeSpan.TicksPerSecond;
        private const long Start = 1000 * S;

        [TestMethod]
        public void TestAtRestKeepsTheOffset()
        {
            var c = PimpWarpClock.AtRest(3, Start + 50 * S, 7000);
            Assert.AreEqual(7000, c.OffsetAt(Start + 500 * S, Start));
            Assert.AreEqual(500 + 7000, c.UniverseTime(Start + 500 * S, Start));
            Assert.IsFalse(c.IsWarping);
        }

        [TestMethod]
        public void TestOpenWarpRunsAtTheRate()
        {
            // From t=100 s at 1000x: after 10 real seconds the universe has gone 10,000 s.
            var c = new PimpWarpClock(0, Start + 100 * S, 5000, 1000, double.NaN);
            var u0 = c.UniverseTime(Start + 100 * S, Start);
            Assert.AreEqual(100 + 5000, u0, 1e-9);
            Assert.AreEqual(u0 + 10000, c.UniverseTime(Start + 110 * S, Start), 1e-6);
            Assert.IsFalse(c.HasLanded(Start + 1000000 * S, Start));
        }

        [TestMethod]
        public void TestWarpLandsOnItsTargetAndStays()
        {
            // At 1000x from u0 = 5100 to target 25100: lands 20 real seconds after T0, then 1x.
            var c = new PimpWarpClock(0, Start + 100 * S, 5000, 1000, 25100);
            Assert.AreEqual(20, c.LandsAfterS(Start), 1e-9);
            Assert.IsFalse(c.HasLanded(Start + 119 * S, Start));
            Assert.IsTrue(c.HasLanded(Start + 120 * S, Start));
            Assert.AreEqual(25100, c.UniverseTime(Start + 120 * S, Start), 1e-6);
            Assert.AreEqual(25100 + 30, c.UniverseTime(Start + 150 * S, Start), 1e-6);
            Assert.AreEqual(5000 + 999 * 20, c.OffsetAt(Start + 500 * S, Start), 1e-6);
        }

        [TestMethod]
        public void TestNeverBackwards()
        {
            // A target already behind: lands at once, the universe keeps going at 1x.
            var c = new PimpWarpClock(0, Start + 100 * S, 5000, 1000, 1000);
            Assert.AreEqual(0, c.LandsAfterS(Start));
            Assert.AreEqual(5000, c.OffsetAt(Start + 200 * S, Start));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void TestRateBelowOneRefused() => new PimpWarpClock(0, Start, 0, 0.5, double.NaN);

        [TestMethod]
        public void TestClockMessageRoundTrip()
        {
            var factory = new ServerMessageFactory();
            var client = new NetClient(new NetPeerConfiguration("TESTS"));
            var d = factory.CreateNewMessageData<WarpClockMsgData>();
            d.From(new PimpWarpClock(9, 638000000000000000, 731234.5, 10000, 800000.25));
            var msg = factory.CreateNew<WarpSrvMsg>(d);
            var send = client.CreateMessage(msg.GetMessageSize());
            msg.Serialize(send);
            var bytes = send.ReadBytes(send.LengthBytes);
            var recv = client.CreateIncomingMessage(NetIncomingMessageType.Data, bytes);
            recv.LengthBytes = send.LengthBytes;
            var r = ((WarpClockMsgData)factory.Deserialize(recv, Environment.TickCount).Data).ToClock();
            Assert.AreEqual(9, r.SubspaceKey);
            Assert.AreEqual(638000000000000000, r.T0Ticks);
            Assert.AreEqual(731234.5, r.Offset0);
            Assert.AreEqual(10000, r.Rate);
            Assert.AreEqual(800000.25, r.Target);
        }
    }
}
