using LmpCommon.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.Context;
using Server.Pimp;
using Server.Settings.Structures;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ServerTest
{
    // PiMP consensus warp W4.1 (25 Oct 2026): the server's warp clock drives UniverseTime.
    [TestClass]
    public class PimpConsensusWarpTest
    {
        [TestMethod]
        public void TestWarpClockMovesUniverseTimeAndLands()
        {
            var saved = new Dictionary<int, Subspace>(WarpContext.Subspaces);
            var savedMode = WarpSettings.SettingsStore.WarpMode;
            var savedStart = TimeContext.StartTime;
            try
            {
                WarpContext.Subspaces.Clear();
                WarpContext.Subspaces.TryAdd(0, new Subspace(0, 100, "Server"));
                TimeContext.StartTime = LmpCommon.Time.LunaNetworkTime.UtcNow.AddSeconds(-1000);

                WarpSettings.SettingsStore.WarpMode = WarpMode.Subspace;
                StringAssert.StartsWith(PimpConsensusWarp.Start(1000, 3000), "not started");

                WarpSettings.SettingsStore.WarpMode = WarpMode.Consensus;
                var u0 = WarpContext.UniverseTime;
                StringAssert.StartsWith(PimpConsensusWarp.Start(1000, 3000), "started");
                StringAssert.StartsWith(PimpConsensusWarp.Start(1000, 3000), "not started");   // one at a time
                Thread.Sleep(1000);
                var u1 = WarpContext.UniverseTime;
                Assert.IsTrue(u1 - u0 > 900 && u1 - u0 < 1500, $"after 1 s at 1000x: {u1 - u0}");

                // Lands on u0 + 3000 about 3 s after the start, then runs at 1x with the offset frozen.
                Thread.Sleep(3000);
                var u2 = WarpContext.UniverseTime;
                Assert.IsTrue(u2 - (u0 + 3000) >= 0 && u2 - (u0 + 3000) < 3, $"landed: {u2 - (u0 + 3000)} s past the target");
                Assert.IsFalse(PimpConsensusWarp.Clock.IsWarping);
                Assert.AreEqual(PimpConsensusWarp.Clock.Offset0, WarpContext.Subspaces[0].Time, 1e-6);

                // An open warp, then a stop: lands 2 real seconds ahead.
                StringAssert.StartsWith(PimpConsensusWarp.Start(100, double.NaN), "started");
                StringAssert.StartsWith(PimpConsensusWarp.Stop(), "stopping");
                var target = PimpConsensusWarp.Clock.Target;
                Thread.Sleep(2600);
                Assert.IsFalse(PimpConsensusWarp.Clock.IsWarping);
                var u3 = WarpContext.UniverseTime;
                Assert.IsTrue(u3 >= target && u3 - target < 2, $"stop landed {u3 - target} s past its target");
            }
            finally
            {
                WarpSettings.SettingsStore.WarpMode = savedMode;
                TimeContext.StartTime = savedStart;
                WarpContext.Subspaces.Clear();
                foreach (var kv in saved) WarpContext.Subspaces.TryAdd(kv.Key, kv.Value);
            }
        }
    }
}
