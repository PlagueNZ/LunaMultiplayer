using LmpClient.Network;
using LmpClient.Systems.ModApi;
using LmpCommon.Message.Data;
using LmpCommon.Pimp;
using System;
using System.Collections.Generic;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP part catalogue, game side (10 Nov 2026; see LmpCommon.Pimp.PimpPartCatalogMsg). Once per
    /// join, when the game is up: the name and title of every part the game has loaded (PartLoader),
    /// whether any vessel uses it or not, sent to the server in a few reliable mod messages. The
    /// server keeps them for the dashboards. Never stops a join: any failure is logged and dropped.
    /// </summary>
    internal static class PimpPartCatalog
    {
        public static void Send()
        {
            try
            {
                var loaded = PartLoader.LoadedPartsList;
                if (loaded == null || loaded.Count == 0) return;

                var parts = new List<KeyValuePair<string, string>>(loaded.Count);
                foreach (var part in loaded)
                    if (part != null && !string.IsNullOrEmpty(part.name))
                        parts.Add(new KeyValuePair<string, string>(part.name, part.title ?? string.Empty));

                var messages = PimpPartCatalogMsg.Build(Guid.NewGuid().ToString("N").Substring(0, 8), parts);
                foreach (var bytes in messages)
                {
                    var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<ModMsgData>();
                    if (msgData.Data.Length < bytes.Length) msgData.Data = new byte[bytes.Length];
                    Array.Copy(bytes, msgData.Data, bytes.Length);
                    msgData.NumBytes = bytes.Length;
                    msgData.Relay = false;
                    msgData.Reliable = true;
                    msgData.ModName = PimpPartCatalogMsg.ModName;
                    ModApiSystem.Singleton.MessageSender.SendMessage(msgData);
                }
                LunaLog.Log($"[LMP]: [PiMP] Part catalogue sent: {parts.Count} part(s) in {messages.Count} message(s)");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: [PiMP] Part catalogue not sent: {e.Message}");
            }
        }
    }
}
