using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;

namespace Server.Context
{
    public class Subspace
    {
        public int Id { get; set; }
        public double Time { get; set; }
        public string Creator { get; set; }

        public override string ToString()
        {
            return $"{Id}:{Time.ToString(CultureInfo.InvariantCulture)}";
        }

        public Subspace(int id) : this(id, 0, "Server") { }

        public Subspace(int id, double time) : this(id, time, "Server") { }

        public Subspace(int id, double time, string creator)
        {
            Id = id;
            Time = time;
            Creator = creator;
        }
    }

    public class WarpContext
    {
        public static volatile int NextSubspaceId;

        public static readonly ConcurrentDictionary<int, Subspace> Subspaces = new ConcurrentDictionary<int, Subspace>();

        public static Subspace LatestSubspace => Subspaces.OrderByDescending(s => s.Value.Time).Select(s => s.Value).First();

        /// <summary>
        /// PiMP (W1, 21 Oct 2026): the universe's game time, in seconds since StartTime on LMP's network
        /// clock, plus the most advanced subspace's offset. LMP never removes the latest subspace and saves
        /// it in Subspace.txt, so this only moves forward. Under WarpMode None the only subspace is 0 at
        /// offset 0. Same formula the bridge's PrincipiaWorker.GameTime() has used since 0.5.1.
        /// </summary>
        public static double UniverseTime =>
            (LmpCommon.Time.LunaNetworkTime.UtcNow - TimeContext.StartTime).TotalSeconds + LatestSubspaceTime;

        /// <summary>The most advanced subspace's offset, 0 with no subspaces (or emptied mid-read).</summary>
        public static double LatestSubspaceTime
        {
            get
            {
                try { return Subspaces.IsEmpty ? 0 : LatestSubspace.Time; }
                catch (global::System.InvalidOperationException) { return 0; }
            }
        }
    }
}
