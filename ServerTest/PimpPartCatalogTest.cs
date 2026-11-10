using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.Pimp;
using System;
using System.Collections.Generic;

namespace ServerTest
{
    // PiMP part catalogue (10 Nov 2026): a send is complete only when every message of it has arrived.
    [TestClass]
    public class PimpPartCatalogTest
    {
        private static List<KeyValuePair<string, string>> One(string name) =>
            new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(name, name + " title") };

        [TestMethod]
        public void TestASendCompletesWhenAllMessagesArriveInAnyOrder()
        {
            var now = DateTime.UtcNow;
            Assert.IsNull(PimpPartCatalog.Receive("p1", "s1", 2, 3, One("c"), now));
            Assert.IsNull(PimpPartCatalog.Receive("p1", "s1", 0, 3, One("a"), now));
            Assert.IsNull(PimpPartCatalog.Receive("p2", "s1", 1, 3, One("x"), now));   // another player's send with the same id
            var done = PimpPartCatalog.Receive("p1", "s1", 1, 3, One("b"), now);
            Assert.IsNotNull(done);
            Assert.AreEqual("a,b,c", string.Join(",", done.ConvertAll(p => p.Key)));
        }

        [TestMethod]
        public void TestAnUnfinishedSendIsDroppedAfterTheTimeout()
        {
            var now = DateTime.UtcNow;
            Assert.IsNull(PimpPartCatalog.Receive("p3", "old", 0, 2, One("a"), now));
            // Three minutes later the first half is gone, so the second half alone does not complete it.
            Assert.IsNull(PimpPartCatalog.Receive("p3", "old", 1, 2, One("b"), now.AddMinutes(3)));
        }
    }
}
