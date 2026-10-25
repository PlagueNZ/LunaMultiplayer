using Lidgren.Network;
using LmpCommon.Message.Types;
using LmpCommon.Pimp;

namespace LmpCommon.Message.Data.Warp
{
    /// <summary>
    /// PiMP consensus warp (plan W4.1, 25 Oct 2026): the warp clock of a subspace (see
    /// <see cref="PimpWarpClock"/>). Server to client: sent to everyone when a consensus warp starts,
    /// is stopped, or lands, and to a joining game after its subspace list.
    /// </summary>
    public class WarpClockMsgData : WarpBaseMsgData
    {
        /// <inheritdoc />
        internal WarpClockMsgData() { }
        public override WarpMessageType WarpMessageType => WarpMessageType.Clock;

        public int SubspaceKey;
        public long T0Ticks;
        public double Offset0;
        public double Rate;
        public double Target;

        public override string ClassName { get; } = nameof(WarpClockMsgData);

        public PimpWarpClock ToClock() => new PimpWarpClock(SubspaceKey, T0Ticks, Offset0, Rate, Target);

        public void From(PimpWarpClock c)
        {
            SubspaceKey = c.SubspaceKey;
            T0Ticks = c.T0Ticks;
            Offset0 = c.Offset0;
            Rate = c.Rate;
            Target = c.Target;
        }

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(SubspaceKey);
            lidgrenMsg.Write(T0Ticks);
            lidgrenMsg.Write(Offset0);
            lidgrenMsg.Write(Rate);
            lidgrenMsg.Write(Target);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            SubspaceKey = lidgrenMsg.ReadInt32();
            T0Ticks = lidgrenMsg.ReadInt64();
            Offset0 = lidgrenMsg.ReadDouble();
            Rate = lidgrenMsg.ReadDouble();
            Target = lidgrenMsg.ReadDouble();
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + sizeof(int) + sizeof(long) + 3 * sizeof(double);
        }
    }
}
