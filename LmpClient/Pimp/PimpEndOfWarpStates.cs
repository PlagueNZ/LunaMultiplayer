using LmpCommon.Message.Data.Warp;
using System;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP (warp plan W3, 25 Oct 2026): the server's end-of-warp states, as fired by
    /// WarpEvent.onAuthoritativeStates on the Unity thread. A copy: LMP recycles the message
    /// once its handler returns, before the queued event runs.
    /// </summary>
    public class PimpEndOfWarpStates
    {
        public int Subspace;
        public double Ut;
        /// <summary>Received at this game's universal time (Planetarium.GetUniversalTime() when fired).</summary>
        public double ReceivedUt;
        public Guid[] VesselIds;
        /// <summary>Per vessel, LMP's orbit layout: inc, ecc, sma, lan, argPe, mna, epoch, body index.</summary>
        public double[][] Orbits;
        /// <summary>Pass-through data from the server's simulation (PiMP: frame vectors); may be empty.</summary>
        public double[] Frame;

        public static PimpEndOfWarpStates From(WarpAuthoritativeStatesMsgData msg)
        {
            var s = new PimpEndOfWarpStates
            {
                Subspace = msg.SubspaceKey,
                Ut = msg.Ut,
                VesselIds = new Guid[msg.StateCount],
                Orbits = new double[msg.StateCount][],
                Frame = new double[msg.FrameCount]
            };
            for (var i = 0; i < msg.StateCount; i++)
            {
                s.VesselIds[i] = msg.States[i].VesselId;
                s.Orbits[i] = (double[])msg.States[i].Orbit.Clone();
            }
            Array.Copy(msg.Frame, s.Frame, msg.FrameCount);
            return s;
        }
    }
}
