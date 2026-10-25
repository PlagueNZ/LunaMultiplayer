namespace LmpCommon.Enums
{
    public enum WarpMode
    {
        None = 0,
        Subspace = 1,
        // PiMP (consensus warp, plan W4, 25 Oct 2026): everyone stays in one subspace; a warp is
        // proposed, everyone must say yes, and the subspace's clock runs at the agreed rate (LmpCommon.Pimp.PimpWarpClock).
        Consensus = 2
    }
}
