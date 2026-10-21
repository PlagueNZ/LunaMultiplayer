using Server.Client;

namespace Server.Plugin
{
    /// <summary>
    /// PiMP (warp plan W1, 21 Oct 2026): optional warp callbacks for server plugins. A plugin
    /// implements this beside <see cref="ILmpPlugin"/>; plugins that don't are not affected, so
    /// ILmpPlugin itself stays unchanged. The bridge (PrincipiaBridge.Server) uses it to learn
    /// about warps as they happen instead of polling the subspace list.
    /// </summary>
    public interface IPimpWarpPlugin
    {
        /// <summary>A player created a subspace (the end of their warp). Called after it is added.</summary>
        void OnSubspaceCreated(ClientStructure creator, int subspaceId, double serverTimeDifference);

        /// <summary>A player moved between subspaces (-1 = warping). Called after client.Subspace is set,
        /// or, for -1, when the move is relayed.</summary>
        void OnClientSubspaceChanged(ClientStructure client, int oldSubspace, int newSubspace);
    }
}
