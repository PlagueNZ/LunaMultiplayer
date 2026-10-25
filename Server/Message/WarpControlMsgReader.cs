using System;
using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Message.Base;
using Server.System;

namespace Server.Message
{
    public class WarpControlMsgReader : ReaderBase
    {
        private static readonly WarpSystemReceiver WarpReceiver = new WarpSystemReceiver();

        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var messageData = message.Data as WarpBaseMsgData;
            switch (messageData?.WarpMessageType)
            {
                case WarpMessageType.NewSubspace:
                    WarpReceiver.HandleNewSubspace(client, (WarpNewSubspaceMsgData)messageData);
                    break;
                case WarpMessageType.ChangeSubspace:
                    WarpReceiver.HandleChangeSubspace(client, (WarpChangeSubspaceMsgData)messageData);
                    break;
                case WarpMessageType.SubspacesRequest:
                    WarpReceiver.HandleSubspaceRequest(client);
                    //We don't use this message anymore so we can recycle it
                    message.Recycle();
                    break;
                // PiMP (W4.3): consensus warp votes.
                case WarpMessageType.Propose:
                    Pimp.PimpConsensusWarp.HandlePropose(client, (WarpProposeMsgData)messageData);
                    break;
                case WarpMessageType.Vote:
                    Pimp.PimpConsensusWarp.HandleVote(client, (WarpVoteMsgData)messageData);
                    break;
                case WarpMessageType.Report:
                    Pimp.PimpConsensusWarp.HandleReport(client, ((WarpReportMsgData)messageData).Report);
                    break;
                case WarpMessageType.RateRequest:
                    Pimp.PimpConsensusWarp.HandleRateRequest(client, ((WarpRateRequestMsgData)messageData).Rate);
                    break;
                case WarpMessageType.StopRequest:
                    Pimp.PimpConsensusWarp.HandleStopRequest(client);
                    break;
                default:
                    throw new NotImplementedException("Warp Type not implemented");
            }
        }
    }
}
