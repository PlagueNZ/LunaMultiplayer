using System;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP (warp plan W1, 21 Oct 2026): the one place LMP asks a physics mod about a vessel's state.
    /// LMP never references Principia; the bridge client mod registers Principia's implementation
    /// with <see cref="PimpHooks.Register"/>. Until then the no-op default answers "not mine", so LMP
    /// behaves exactly as without it. States are in the inertial (elements) frame, relative to the
    /// vessel's main body, at <c>ut</c> (W0: KSP's world axes rotate with the planet at the space
    /// centre, so anything compared across time must not be in world axes).
    /// </summary>
    public interface IPimpPhysicsHook
    {
        string Name { get; }

        /// <summary>The physics mod's current state of a vessel; false if it does not manage it.</summary>
        bool TryGetState(Guid vesselId, out double ut, out Vector3d q, out Vector3d p);

        /// <summary>Ask the physics mod to take this state for the vessel; false if refused or unmanaged.</summary>
        bool RequestReseed(Guid vesselId, double ut, Vector3d q, Vector3d p);
    }

    public static class PimpHooks
    {
        private sealed class NoPhysicsHook : IPimpPhysicsHook
        {
            public string Name => "none";
            public bool TryGetState(Guid vesselId, out double ut, out Vector3d q, out Vector3d p)
            {
                ut = 0; q = p = Vector3d.zero;
                return false;
            }
            public bool RequestReseed(Guid vesselId, double ut, Vector3d q, Vector3d p) => false;
        }

        public static IPimpPhysicsHook Physics { get; private set; } = new NoPhysicsHook();

        /// <summary>Registers (or, with null, clears) the physics hook. The latest registration wins,
        /// so a hot-reloaded bridge simply registers again.</summary>
        public static void Register(IPimpPhysicsHook hook)
        {
            Physics = hook ?? new NoPhysicsHook();
            LunaLog.Log($"[LMP]: PiMP physics hook: {Physics.Name}");
        }
    }
}
