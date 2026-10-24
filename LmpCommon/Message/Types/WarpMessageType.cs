namespace LmpCommon.Message.Types
{
    public enum WarpMessageType
    {
        SubspacesRequest = 1,
        SubspacesReply = 2,
        NewSubspace = 3,
        ChangeSubspace = 4,
        // PiMP (warp plan W3, 25 Oct 2026): server -> client only.
        AuthoritativeStates = 5
    }
}