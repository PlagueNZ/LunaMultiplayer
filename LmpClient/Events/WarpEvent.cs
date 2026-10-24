using LmpClient.Events.Base;

// ReSharper disable All
#pragma warning disable IDE1006

namespace LmpClient.Events
{
    public class WarpEvent : LmpBaseEvent
    {
        public static EventVoid onTimeWarpStopped;

        // PiMP (warp plan W1, 21 Oct 2026). Both fire on the Unity thread (WarpSystem queues them:
        // warp messages are handled on a network thread). onSubspaceChanged(old, new): this game moved
        // between subspaces (-1 = warping). onWarpCompleted(subspace): the server assigned the subspace
        // this game created at the end of its own warp.
        public static EventData<int, int> onSubspaceChanged;
        public static EventData<int> onWarpCompleted;

        // PiMP (warp plan W3, 25 Oct 2026): the server's states of every vessel nobody flies, at one
        // game time just after it caught up with a warp that ended in this game's subspace. Fired on
        // the Unity thread with a copy of the message.
        public static EventData<Pimp.PimpEndOfWarpStates> onAuthoritativeStates;
    }
}
