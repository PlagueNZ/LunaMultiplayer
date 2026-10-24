using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Server;
using System.Collections.Generic;

namespace Server.Pimp
{
    /// <summary>
    /// PiMP (warp plan W3, 25 Oct 2026): the server half of the end-of-warp agreement. LMP knows
    /// when a warp ends (a new subspace) but not where the vessels are at that moment; a plugin
    /// that runs the simulation (the bridge, through <see cref="Plugin.IPimpWarpPlugin"/>) catches
    /// up and then calls <see cref="Send"/> with its states at one game time. LMP delivers them,
    /// reliably and in order with the other warp messages, to every client in the subspace.
    /// </summary>
    public static class PimpWarpStates
    {
        /// <summary>One vessel's orbit: LMP's layout (inc, ecc, sma, lan, argPe, mna, epoch, body index).</summary>
        public struct State
        {
            public global::System.Guid VesselId;
            public double[] Orbit;
        }

        /// <summary>
        /// Sends <paramref name="states"/> at game time <paramref name="ut"/> to every client in
        /// <paramref name="subspaceId"/>. <paramref name="frame"/> is passed through unread (may be null).
        /// Returns the number of clients it was sent to. Safe to call from any thread.
        /// </summary>
        public static int Send(int subspaceId, double ut, IReadOnlyList<State> states, double[] frame)
        {
            var sent = 0;
            foreach (var client in ClientRetriever.GetClients())
            {
                if (client == null || !client.Authenticated || client.Subspace != subspaceId) continue;
                var msg = ServerContext.ServerMessageFactory.CreateNewMessageData<WarpAuthoritativeStatesMsgData>();
                msg.SubspaceKey = subspaceId;
                msg.Ut = ut;
                msg.StateCount = states.Count;
                if (msg.States.Length < states.Count) msg.States = new PimpVesselState[states.Count];
                for (var i = 0; i < states.Count; i++)
                {
                    if (msg.States[i] == null) msg.States[i] = new PimpVesselState();
                    msg.States[i].VesselId = states[i].VesselId;
                    for (var k = 0; k < 8; k++) msg.States[i].Orbit[k] = states[i].Orbit[k];
                }
                msg.FrameCount = frame?.Length ?? 0;
                if (msg.Frame.Length < msg.FrameCount) msg.Frame = new double[msg.FrameCount];
                for (var i = 0; i < msg.FrameCount; i++) msg.Frame[i] = frame[i];
                MessageQueuer.SendToClient<WarpSrvMsg>(client, msg);
                sent++;
            }
            LunaLog.Normal($"[PiMP] End-of-warp states: {states.Count} vessel(s) at t={ut:F1} sent to {sent} client(s) in subspace {subspaceId}");
            return sent;
        }
    }
}
