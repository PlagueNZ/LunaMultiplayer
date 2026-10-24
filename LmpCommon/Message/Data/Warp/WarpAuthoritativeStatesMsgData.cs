using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.Warp
{
    /// <summary>
    /// PiMP (warp plan W3, 25 Oct 2026): the end-of-warp agreement. After a player ends a warp the
    /// server's simulation catches up to the new subspace; it then sends that player (and anyone
    /// else in the subspace) its own state of every vessel nobody is flying, all at one game time
    /// <see cref="Ut"/>, so the game can compare its own states with the server's at the end of
    /// the warp instead of waiting for a later refresh. The states are orbits in the same
    /// elements LMP stores (inclination, eccentricity, semi-major axis, LAN, argument of
    /// periapsis, mean anomaly, epoch, reference body index). <see cref="Frame"/> is optional
    /// data from whatever produced the states, passed through unread by LMP (PiMP: the server's
    /// frame vectors, so the game can turn the elements into its own frame).
    /// Server to client only. Sent by Server.Pimp.PimpWarpStates; fired on the client as
    /// WarpEvent.onAuthoritativeStates.
    /// </summary>
    public class WarpAuthoritativeStatesMsgData : WarpBaseMsgData
    {
        /// <inheritdoc />
        internal WarpAuthoritativeStatesMsgData() { }
        public override WarpMessageType WarpMessageType => WarpMessageType.AuthoritativeStates;

        public int SubspaceKey;
        public double Ut;
        public int StateCount;
        public PimpVesselState[] States = new PimpVesselState[0];
        public int FrameCount;
        public double[] Frame = new double[0];

        public override string ClassName { get; } = nameof(WarpAuthoritativeStatesMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);

            lidgrenMsg.Write(SubspaceKey);
            lidgrenMsg.Write(Ut);
            lidgrenMsg.Write(StateCount);
            for (var i = 0; i < StateCount; i++)
                States[i].Serialize(lidgrenMsg);
            lidgrenMsg.Write(FrameCount);
            for (var i = 0; i < FrameCount; i++)
                lidgrenMsg.Write(Frame[i]);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);

            SubspaceKey = lidgrenMsg.ReadInt32();
            Ut = lidgrenMsg.ReadDouble();
            StateCount = lidgrenMsg.ReadInt32();
            if (States.Length < StateCount)
                States = new PimpVesselState[StateCount];
            for (var i = 0; i < StateCount; i++)
            {
                if (States[i] == null)
                    States[i] = new PimpVesselState();
                States[i].Deserialize(lidgrenMsg);
            }
            FrameCount = lidgrenMsg.ReadInt32();
            if (Frame.Length < FrameCount)
                Frame = new double[FrameCount];
            for (var i = 0; i < FrameCount; i++)
                Frame[i] = lidgrenMsg.ReadDouble();
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + sizeof(int) + sizeof(double) + sizeof(int) +
                   StateCount * PimpVesselState.ByteCount + sizeof(int) + FrameCount * sizeof(double);
        }
    }

    /// <summary>PiMP (W3): one vessel's orbit at <see cref="WarpAuthoritativeStatesMsgData.Ut"/>.</summary>
    public class PimpVesselState
    {
        public System.Guid VesselId;
        /// <summary>Inclination, eccentricity, semi-major axis, LAN, argument of periapsis,
        /// mean anomaly at epoch, epoch, reference body index: LMP's orbit layout.</summary>
        public double[] Orbit = new double[8];

        public const int ByteCount = 16 + 8 * sizeof(double);

        public void Serialize(NetOutgoingMessage lidgrenMsg)
        {
            GuidUtil.Serialize(VesselId, lidgrenMsg);
            for (var i = 0; i < 8; i++)
                lidgrenMsg.Write(Orbit[i]);
        }

        public void Deserialize(NetIncomingMessage lidgrenMsg)
        {
            VesselId = GuidUtil.Deserialize(lidgrenMsg);
            for (var i = 0; i < 8; i++)
                Orbit[i] = lidgrenMsg.ReadDouble();
        }
    }
}
