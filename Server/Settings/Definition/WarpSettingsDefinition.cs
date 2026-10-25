using LmpCommon.Enums;
using LmpCommon.Xml;
using System;

namespace Server.Settings.Definition
{
    [Serializable]
    public class WarpSettingsDefinition
    {
        [XmlComment(Value = "Specify the warp Type. Values: None, Subspace, Consensus (PiMP: everyone warps together, by vote)")]
        public WarpMode WarpMode { get; set; } = WarpMode.Subspace;

        [XmlComment(Value = "PiMP, WarpMode Consensus: seconds a player has to answer a warp request; no answer counts as no")]
        public int ConsensusVoteTimeoutS { get; set; } = 30;

        [XmlComment(Value = "PiMP, WarpMode Consensus: ask a player who is alone too (shows the popup, for testing)")]
        public bool ConsensusAskWhenAlone { get; set; } = false;

        [XmlComment(Value = "PiMP, WarpMode Consensus: the fastest group warp allowed, whatever the games can do (BetterTimeWarp goes to millions)")]
        public double ConsensusMaxRate { get; set; } = 100000;
    }
}
