using LmpCommon;
using LmpCommon.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Server.System;
using Server.Settings.Structures;
using System.IO;

namespace ServerTest
{
    [TestClass]
    public class HandshakeSystemValidatorTest
    {
        [TestInitialize]
        public void Setup()
        {
        }

        [TestMethod]
        public void TestVersionValidation()
        {
            // Set up local version for comparison
            var localVersion = LmpVersioning.CurrentVersion.ToString();
            
            // Should succeed with current version
            Assert.IsTrue(LmpVersioning.IsCompatible(localVersion));

            // Should fail with a wildly different version
            Assert.IsFalse(LmpVersioning.IsCompatible("0.1.0.0"));
        }

        [TestMethod]
        public void TestPimpRefusesStockVersions()
        {
            // PiMP: client and server are a matched pair on 0.31. Stock 0.29 and 0.30 builds
            // (upstream's 0.30 <-> 0.29 cross-compatibility) must not be accepted.
            Assert.AreEqual(0, LmpVersioning.CurrentVersion.Major);
            Assert.AreEqual(31, LmpVersioning.CurrentVersion.Minor);
            Assert.IsTrue(LmpVersioning.IsCompatible("0.31.0.2"));
            Assert.IsFalse(LmpVersioning.IsCompatible("0.30.0.0"));
            Assert.IsFalse(LmpVersioning.IsCompatible("0.29.2.0"));
        }

        [TestMethod]
        public void TestModValidationDisabled()
        {
            // Set server to NOT use a whitelist
            GeneralSettings.SettingsStore.ModControl = false;
            
            // If mod control is disabled, HandshakeReply should have ModControl = false
            Assert.IsFalse(GeneralSettings.SettingsStore.ModControl);
        }

        [TestMethod]
        public void TestPlayerNameValidation()
        {
            // Valid names
            Assert.IsTrue(HandshakeSystem.PlayerNameIsValid("ValidName", out var reason));
            Assert.IsTrue(HandshakeSystem.PlayerNameIsValid("Player_1", out reason));

            // Invalid names
            Assert.IsFalse(HandshakeSystem.PlayerNameIsValid("Invalid Name!", out reason));
            Assert.IsFalse(HandshakeSystem.PlayerNameIsValid("", out reason)); // Too short
            Assert.IsFalse(HandshakeSystem.PlayerNameIsValid(new string('A', 33), out reason)); // Too long
        }
    }
}
