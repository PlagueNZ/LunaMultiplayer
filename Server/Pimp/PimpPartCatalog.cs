using LmpCommon.Pimp;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Server.Pimp
{
    /// <summary>
    /// PiMP part catalogue, server side (10 Nov 2026; see LmpCommon.Pimp.PimpPartCatalogMsg). Collects
    /// the messages of one send from a game; when all have arrived, adds its parts to the catalogue
    /// and writes Universe/PartCatalog.json ({ "catalog": { name: title }, "receivedAt": ms, "from":
    /// player }), the file the dashboards read. Games add to the catalogue (two players with
    /// different optional mods both count); a title a game sends replaces the stored one. Written
    /// only when something changed, to a temporary file first.
    /// </summary>
    public static class PimpPartCatalog
    {
        private sealed class Pending
        {
            public DateTime Started = DateTime.UtcNow;
            public List<KeyValuePair<string, string>>[] Messages;
        }

        private static readonly object Lock = new object();
        private static readonly Dictionary<string, string> Catalog = new Dictionary<string, string>();
        private static readonly Dictionary<string, Pending> Sends = new Dictionary<string, Pending>();
        private static readonly TimeSpan SendTimeout = TimeSpan.FromMinutes(2);
        private const int MaxSends = 16;

        public static string FilePath => Path.Combine(ServerContext.UniverseDirectory, "PartCatalog.json");

        public static void Register()
        {
            try
            {
                lock (Lock)
                {
                    Catalog.Clear();
                    Sends.Clear();
                    if (File.Exists(FilePath))
                    {
                        using (var doc = JsonDocument.Parse(File.ReadAllText(FilePath)))
                            if (doc.RootElement.TryGetProperty("catalog", out var c) && c.ValueKind == JsonValueKind.Object)
                                foreach (var p in c.EnumerateObject()) Catalog[p.Name] = p.Value.GetString() ?? string.Empty;
                    }
                }
                var ok = LmpModInterface.RegisterModHandler(PimpPartCatalogMsg.ModName, OnMessage);
                LunaLog.Normal($"[PiMP] Part catalogue: {Catalog.Count} part(s) on file; handler {(ok ? "registered" : "NOT registered (name taken)")}");
            }
            catch (Exception e)
            {
                LunaLog.Error($"[PiMP] Part catalogue not started: {e.Message}");
            }
        }

        public static void OnMessage(ClientStructure client, byte[] data, int numBytes)
        {
            try
            {
                if (!PimpPartCatalogMsg.TryParse(data, numBytes, out var sendId, out var index, out var count, out var parts))
                {
                    LunaLog.Debug($"[PiMP] Part catalogue: unreadable message from {client?.PlayerName}");
                    return;
                }
                var player = client?.PlayerName ?? "?";
                var done = Receive(player, sendId, index, count, parts, DateTime.UtcNow);
                if (done != null) Apply(player, done);
            }
            catch (Exception e)
            {
                LunaLog.Error($"[PiMP] Part catalogue: error handling a message from {client?.PlayerName}: {e.Message}");
            }
        }

        /// <summary>Adds one message to its send; returns every part of the send once it is complete, else null.</summary>
        public static List<KeyValuePair<string, string>> Receive(string player, string sendId, int index, int count,
            List<KeyValuePair<string, string>> parts, DateTime now)
        {
            lock (Lock)
            {
                foreach (var old in Sends.Where(s => now - s.Value.Started > SendTimeout).Select(s => s.Key).ToList()) Sends.Remove(old);
                var key = player + "|" + sendId;
                if (!Sends.TryGetValue(key, out var send))
                {
                    if (Sends.Count >= MaxSends) return null;
                    Sends[key] = send = new Pending { Started = now, Messages = new List<KeyValuePair<string, string>>[count] };
                }
                if (send.Messages.Length != count) return null;
                send.Messages[index] = parts;
                if (send.Messages.Any(m => m == null)) return null;
                Sends.Remove(key);
                return send.Messages.SelectMany(m => m).ToList();
            }
        }

        private static void Apply(string player, List<KeyValuePair<string, string>> parts)
        {
            int added = 0, changed = 0, total;
            string json = null;
            lock (Lock)
            {
                foreach (var p in parts)
                {
                    if (p.Key.Length == 0 || (Catalog.Count >= PimpPartCatalogMsg.MaxParts && !Catalog.ContainsKey(p.Key))) continue;
                    if (!Catalog.TryGetValue(p.Key, out var had)) added++;
                    else if (had != p.Value) changed++;
                    else continue;
                    Catalog[p.Key] = p.Value;
                }
                total = Catalog.Count;
                if (added + changed > 0)
                    json = JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        ["catalog"] = new SortedDictionary<string, string>(Catalog, StringComparer.Ordinal),
                        ["receivedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        ["from"] = player,
                    });
            }
            if (json != null)
            {
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, true);
            }
            LunaLog.Normal($"[PiMP] Part catalogue from {player}: {parts.Count} part(s), {added} new, {changed} changed; {total} on file{(json == null ? " (unchanged, not written)" : "")}");
        }
    }
}
