using LmpCommon.Pimp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LmpCommonTest
{
    // PiMP part catalogue (10 Nov 2026): the messages a game sends and what the server reads back.
    [TestClass]
    public class PimpPartCatalogMsgTest
    {
        private static List<KeyValuePair<string, string>> Parts(int n) =>
            Enumerable.Range(0, n).Select(i => new KeyValuePair<string, string>("part." + i, "Title " + i)).ToList();

        [TestMethod]
        public void TestRoundTripOverSeveralMessages()
        {
            var messages = PimpPartCatalogMsg.Build("abc123", Parts(2 * PimpPartCatalogMsg.PartsPerMessage + 7));
            Assert.AreEqual(3, messages.Count);
            var back = new List<KeyValuePair<string, string>>();
            for (var i = 0; i < messages.Count; i++)
            {
                Assert.IsTrue(PimpPartCatalogMsg.TryParse(messages[i], messages[i].Length, out var id, out var index, out var count, out var parts));
                Assert.AreEqual("abc123", id);
                Assert.AreEqual(i, index);
                Assert.AreEqual(3, count);
                back.AddRange(parts);
            }
            CollectionAssert.AreEqual(Parts(2 * PimpPartCatalogMsg.PartsPerMessage + 7), back);
        }

        [TestMethod]
        public void TestSeparatorsInATitleDoNotBreakTheMessage()
        {
            var messages = PimpPartCatalogMsg.Build("id", new[] { new KeyValuePair<string, string>("tank", "Tank\tMk1\nlong | wide"), new KeyValuePair<string, string>("", "no name") });
            Assert.AreEqual(1, messages.Count);
            Assert.IsTrue(PimpPartCatalogMsg.TryParse(messages[0], messages[0].Length, out _, out _, out _, out var parts));
            Assert.AreEqual(1, parts.Count);
            Assert.AreEqual("tank", parts[0].Key);
            Assert.AreEqual("Tank Mk1 long / wide", parts[0].Value);
        }

        [TestMethod]
        public void TestMalformedMessagesAreRefused()
        {
            foreach (var text in new[] { "", "plans|1|x|0|1", "parts|2|x|0|1", "parts|1||0|1", "parts|1|x|1|1", "parts|1|x|0|0", "parts|1|x|0|999999", "parts|1|x|a|1" })
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                Assert.IsFalse(PimpPartCatalogMsg.TryParse(bytes, bytes.Length, out _, out _, out _, out _), text);
            }
        }

        [TestMethod]
        public void TestAnEmptyCatalogueSendsNothing()
        {
            Assert.AreEqual(0, PimpPartCatalogMsg.Build("id", Parts(0)).Count);
        }
    }
}
