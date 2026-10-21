using LmpCommon.Message.Interface;
using LmpCommon.Time;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.Client;
using Server.Context;
using Server.Plugin;
using System;
using System.Collections.Generic;

namespace ServerTest
{
    // PiMP warp plan W1 (21 Oct 2026): the optional warp callbacks and the universe-time API.
    [TestClass]
    public class PimpWarpHooksTest
    {
        private sealed class WarpAware : LmpPlugin, IPimpWarpPlugin
        {
            public readonly List<string> Calls = new List<string>();
            public bool Throw;
            public void OnSubspaceCreated(ClientStructure creator, int subspaceId, double serverTimeDifference)
            {
                Calls.Add($"created {subspaceId} {serverTimeDifference}");
                if (Throw) throw new InvalidOperationException("test");
            }
            public void OnClientSubspaceChanged(ClientStructure client, int oldSubspace, int newSubspace) =>
                Calls.Add($"changed {oldSubspace}->{newSubspace}");
        }

        private sealed class Plain : LmpPlugin { }

        [TestMethod]
        public void TestWarpCallbacksReachOnlyWarpAwarePlugins()
        {
            var aware = new WarpAware();
            var plain = new Plain();
            LmpPluginHandler.AddPluginForTest(plain);
            LmpPluginHandler.AddPluginForTest(aware);
            try
            {
                LmpPluginHandler.FireOnSubspaceCreated(null, 4, 5723.5);
                LmpPluginHandler.FireOnClientSubspaceChanged(null, 3, -1);
                LmpPluginHandler.FireOnClientSubspaceChanged(null, -1, 4);
                CollectionAssert.AreEqual(new[] { "created 4 5723.5", "changed 3->-1", "changed -1->4" }, aware.Calls);
            }
            finally
            {
                LmpPluginHandler.RemovePluginForTest(plain);
                LmpPluginHandler.RemovePluginForTest(aware);
            }
        }

        [TestMethod]
        public void TestThrowingWarpPluginDoesNotStopTheOthers()
        {
            var first = new WarpAware { Throw = true };
            var second = new WarpAware();
            LmpPluginHandler.AddPluginForTest(first);
            LmpPluginHandler.AddPluginForTest(second);
            try
            {
                LmpPluginHandler.FireOnSubspaceCreated(null, 7, 1.0);
                Assert.AreEqual(1, first.Calls.Count);
                Assert.AreEqual(1, second.Calls.Count);
            }
            finally
            {
                LmpPluginHandler.RemovePluginForTest(first);
                LmpPluginHandler.RemovePluginForTest(second);
            }
        }

        [TestMethod]
        public void TestUniverseTimeIsClockPlusLatestSubspace()
        {
            var saved = new Dictionary<int, Subspace>(WarpContext.Subspaces);
            var savedStart = TimeContext.StartTime;
            try
            {
                WarpContext.Subspaces.Clear();
                TimeContext.StartTime = LunaNetworkTime.UtcNow.AddSeconds(-1000);
                Assert.AreEqual(0, WarpContext.LatestSubspaceTime);
                Assert.AreEqual(1000, WarpContext.UniverseTime, 5);

                WarpContext.Subspaces.TryAdd(0, new Subspace(0, 0));
                WarpContext.Subspaces.TryAdd(3, new Subspace(3, 45249.08, "Plague"));
                WarpContext.Subspaces.TryAdd(4, new Subspace(4, 50972.5, "Plague"));
                Assert.AreEqual(50972.5, WarpContext.LatestSubspaceTime);
                Assert.AreEqual(1000 + 50972.5, WarpContext.UniverseTime, 5);
            }
            finally
            {
                WarpContext.Subspaces.Clear();
                foreach (var kv in saved) WarpContext.Subspaces.TryAdd(kv.Key, kv.Value);
                TimeContext.StartTime = savedStart;
            }
        }
    }
}
