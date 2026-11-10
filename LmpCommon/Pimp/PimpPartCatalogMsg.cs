using System;
using System.Collections.Generic;
using System.Text;

namespace LmpCommon.Pimp
{
    /// <summary>
    /// PiMP part catalogue (10 Nov 2026): part name -> the title the game shows for it. A save only
    /// holds part names; the titles exist in the part configs a game has loaded, and the server has
    /// no GameData. So each game sends its catalogue once per join as mod messages (no new message
    /// type, so older games and servers are unaffected), the server keeps it in
    /// Universe/PartCatalog.json, and the dashboards read real titles from there. It replaces the
    /// separate dashboard plugin's HTTP post of the same list.
    /// One message, UTF-8 text: "parts|1|&lt;send id&gt;|&lt;index&gt;|&lt;count&gt;" then one "name TAB title" per line.
    /// </summary>
    public static class PimpPartCatalogMsg
    {
        public const string ModName = "PimpPartCatalog";
        public const int PartsPerMessage = 150;
        public const int MaxParts = 20000;
        public const int MaxMessages = MaxParts / PartsPerMessage + 1;
        public const int MaxTextLength = 200;

        /// <summary>The messages for one catalogue; parts with no name are left out.</summary>
        public static List<byte[]> Build(string sendId, IEnumerable<KeyValuePair<string, string>> parts)
        {
            var lines = new List<string>();
            foreach (var p in parts)
            {
                var name = Clean(p.Key);
                if (name.Length == 0 || lines.Count >= MaxParts) continue;
                lines.Add(name + "\t" + Clean(p.Value));
            }

            var messages = new List<byte[]>();
            var count = (lines.Count + PartsPerMessage - 1) / PartsPerMessage;
            for (var i = 0; i < count; i++)
            {
                var sb = new StringBuilder();
                sb.Append("parts|1|").Append(Clean(sendId)).Append('|').Append(i).Append('|').Append(count);
                for (var j = i * PartsPerMessage; j < Math.Min(lines.Count, (i + 1) * PartsPerMessage); j++)
                    sb.Append('\n').Append(lines[j]);
                messages.Add(Encoding.UTF8.GetBytes(sb.ToString()));
            }
            return messages;
        }

        /// <summary>Reads one message; false if it is not a well-formed catalogue message.</summary>
        public static bool TryParse(byte[] data, int numBytes, out string sendId, out int index, out int count,
            out List<KeyValuePair<string, string>> parts)
        {
            sendId = null; index = 0; count = 0; parts = null;
            if (data == null || numBytes <= 0 || numBytes > data.Length) return false;
            var lines = Encoding.UTF8.GetString(data, 0, numBytes).Split('\n');
            var head = lines[0].Split('|');
            if (head.Length != 5 || head[0] != "parts" || head[1] != "1" || head[2].Length == 0) return false;
            if (!int.TryParse(head[3], out index) || !int.TryParse(head[4], out count)) return false;
            if (count < 1 || count > MaxMessages || index < 0 || index >= count || lines.Length - 1 > PartsPerMessage) return false;

            sendId = head[2];
            parts = new List<KeyValuePair<string, string>>(lines.Length - 1);
            for (var i = 1; i < lines.Length; i++)
            {
                var tab = lines[i].IndexOf('\t');
                if (tab <= 0) continue;
                parts.Add(new KeyValuePair<string, string>(Clean(lines[i].Substring(0, tab)), Clean(lines[i].Substring(tab + 1))));
            }
            return true;
        }

        // One line, no separators, at most MaxTextLength characters.
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ').Replace('|', '/').Trim();
            return s.Length > MaxTextLength ? s.Substring(0, MaxTextLength) : s;
        }
    }
}
