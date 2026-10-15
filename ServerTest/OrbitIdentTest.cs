using LunaConfigNode.CfgNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.System.Vessel;
using System.IO;

namespace ServerTest
{
    /// <summary>
    /// PiMP: ApplyOrbitIdent keeps IDENT's "&lt;system&gt;/" prefix and replaces only the body name,
    /// the same format the PrincipiaBridge server plugin writes.
    /// </summary>
    [TestClass]
    public class OrbitIdentTest
    {
        private static readonly string XmlExamplePath = Path.Combine(Directory.GetCurrentDirectory(), "XmlExampleFiles", "Vessel.txt");

        private static Server.System.Vessel.Classes.Vessel NewVessel(string ident)
        {
            var vessel = new Server.System.Vessel.Classes.Vessel(File.ReadAllText(XmlExamplePath));
            if (ident != null)
                vessel.Orbit.Add(new CfgNodeValue<string, string>("IDENT", ident));
            return vessel;
        }

        [TestMethod]
        public void KeepsSystemPrefix()
        {
            var vessel = NewVessel("Squad/Kerbin");
            VesselDataUpdater.ApplyOrbitIdent(vessel, "Mun");
            Assert.AreEqual("Squad/Mun", vessel.Orbit.GetSingle("IDENT").Value);
        }

        [TestMethod]
        public void PlainIdentIsReplaced()
        {
            var vessel = NewVessel("Kerbin");
            VesselDataUpdater.ApplyOrbitIdent(vessel, "Mun");
            Assert.AreEqual("Mun", vessel.Orbit.GetSingle("IDENT").Value);
        }

        [TestMethod]
        public void MissingIdentIsAdded()
        {
            var vessel = NewVessel(null);
            VesselDataUpdater.ApplyOrbitIdent(vessel, "Mun");
            Assert.AreEqual("Mun", vessel.Orbit.GetSingle("IDENT").Value);
        }

        [TestMethod]
        public void EmptyBodyNameChangesNothing()
        {
            var vessel = NewVessel("Squad/Kerbin");
            VesselDataUpdater.ApplyOrbitIdent(vessel, "");
            Assert.AreEqual("Squad/Kerbin", vessel.Orbit.GetSingle("IDENT").Value);
        }
    }
}
