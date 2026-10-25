namespace Server.Plugin
{
    /// <summary>
    /// PiMP consensus warp (plan W4.2, 26 Oct 2026): optional callback for server plugins that run a
    /// simulation alongside LMP (the bridge). A consensus warp keeps everyone in one subspace, so no
    /// new subspace marks its end (as IPimpWarpPlugin.OnSubspaceCreated does in Subspace mode); this
    /// fires instead when the shared warp clock has landed on its target. Separate from
    /// IPimpWarpPlugin so plugins built before it keep loading.
    /// </summary>
    public interface IPimpConsensusWarpPlugin
    {
        /// <summary>The warp clock of <paramref name="subspaceId"/> landed at universe time <paramref name="universeTime"/>.</summary>
        void OnConsensusWarpLanded(int subspaceId, double universeTime);
    }
}
