namespace LmpCommon.Message.Types
{
    public enum WarpMessageType
    {
        SubspacesRequest = 1,
        SubspacesReply = 2,
        NewSubspace = 3,
        ChangeSubspace = 4,
        // PiMP (warp plan W3, 25 Oct 2026): server -> client only.
        AuthoritativeStates = 5,
        // PiMP (consensus warp, plan W4.1, 25 Oct 2026): server -> client only.
        Clock = 6,
        // PiMP (consensus warp, plan W4.3, 26 Oct 2026): propose and stop (client -> server), ask and
        // result (server -> client), vote and report (client -> server).
        Propose = 7,
        Ask = 8,
        Vote = 9,
        Result = 10,
        Report = 11,
        StopRequest = 12,
        // PiMP (W4.3, Jacob 26 Oct): the proposer sets the group warp's rate while it runs.
        RateRequest = 13
    }
}