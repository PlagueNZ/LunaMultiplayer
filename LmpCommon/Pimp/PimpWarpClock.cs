using System;

namespace LmpCommon.Pimp
{
    /// <summary>
    /// PiMP consensus warp (plan W4.1, 25 Oct 2026): the warp clock of one subspace. In Subspace mode a
    /// subspace's offset never moves, so the only way time moves ahead is a new subspace. In
    /// WarpMode.Consensus everyone stays in one subspace and the server moves its offset at a known
    /// rate while a warp runs, so the server and every game compute the same universe time at every
    /// instant from this definition:
    ///   from server-clock time T0 the offset is Offset0 + (Rate - 1) * (t - T0), in seconds;
    ///   with a Target (a universe time), the warp lands on it and the offset stays where it landed.
    /// Universe time = (t - server start) + offset, the same sum LMP's subspaces use
    /// (WarpSystem.GetSubspaceTime on the client, WarpContext.UniverseTime on the server).
    /// Times are LunaNetworkTime ticks. Immutable: a change is a new clock.
    /// </summary>
    public sealed class PimpWarpClock
    {
        public readonly int SubspaceKey;
        public readonly long T0Ticks;
        public readonly double Offset0;
        public readonly double Rate;
        /// <summary>Universe time the warp lands on; NaN for an open warp (until stopped).</summary>
        public readonly double Target;

        public PimpWarpClock(int subspaceKey, long t0Ticks, double offset0, double rate, double target)
        {
            if (!(rate >= 1)) throw new ArgumentOutOfRangeException(nameof(rate), rate, "rate must be at least 1");
            SubspaceKey = subspaceKey;
            T0Ticks = t0Ticks;
            Offset0 = offset0;
            Rate = rate;
            Target = target;
        }

        /// <summary>A clock at rest (rate 1, no target): the subspace's offset is fixed.</summary>
        public static PimpWarpClock AtRest(int subspaceKey, long nowTicks, double offset) =>
            new PimpWarpClock(subspaceKey, nowTicks, offset, 1, double.NaN);

        public bool IsWarping => Rate > 1;

        private static double Seconds(long ticks) => ticks / (double)TimeSpan.TicksPerSecond;

        /// <summary>Universe time at T0.</summary>
        public double UniverseAtT0(long startTicks) => Seconds(T0Ticks - startTicks) + Offset0;

        /// <summary>Real seconds after T0 at which the warp lands on Target (+inf when open or at rest).</summary>
        public double LandsAfterS(long startTicks)
        {
            if (double.IsNaN(Target) || Rate <= 1) return double.PositiveInfinity;
            return Math.Max(0, (Target - UniverseAtT0(startTicks)) / Rate);
        }

        /// <summary>The subspace's offset at nowTicks.</summary>
        public double OffsetAt(long nowTicks, long startTicks)
        {
            var dt = Math.Max(0, Seconds(nowTicks - T0Ticks));
            return Offset0 + (Rate - 1) * Math.Min(dt, LandsAfterS(startTicks));
        }

        /// <summary>Universe time at nowTicks: seconds since the server's start plus the offset.</summary>
        public double UniverseTime(long nowTicks, long startTicks) => Seconds(nowTicks - startTicks) + OffsetAt(nowTicks, startTicks);

        public bool HasLanded(long nowTicks, long startTicks) => Seconds(nowTicks - T0Ticks) >= LandsAfterS(startTicks);

        public override string ToString() =>
            $"subspace {SubspaceKey}, rate {Rate:G6}x from offset {Offset0:F3} s, " +
            (double.IsNaN(Target) ? "open" : $"landing at t={Target:F1}");
    }
}
