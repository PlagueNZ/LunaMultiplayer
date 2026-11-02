using LmpClient.Systems.Scenario;
using System;
using System.Collections.Generic;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP scenario load order (2 Nov 2026). The server sends scenarios in no fixed order (it reads
    /// them from a dictionary, Server ScenarioSystem.SendScenarioModules), and KSP loads each
    /// ProtoScenarioModule in the order it is added. A mod scenario that reads a stock one while
    /// loading then finds it missing: on the RP-1 test server (lmp-rp1, Career), Kerbalism's OnLoad
    /// came before ResearchAndDevelopment, ScienceDB.Load asked R&D for its subjects, and Kerbalism
    /// stopped the game with a fatal error. A game-made save puts KSP's own scenarios first.
    /// Mods depend on each other's order too: TestFlightRnDScenario.OnAwake logs through
    /// TestFlightManagerScenario.Instance, so it throws on its first line when it comes first, and its
    /// research teams are never set up. So, as in a new game: KSP's own scenarios (from its main
    /// assembly) first, then the mods' in the order KSP lists their scenario types
    /// (KSPScenarioType.GetAllScenarioTypesInAssemblies, which LMP also uses to create missing ones),
    /// then any it does not list, in the order the server sent them.
    /// </summary>
    public static class PimpScenarioOrder
    {
        public static List<ScenarioEntry> StockFirst(IEnumerable<ScenarioEntry> entries, Func<string, Type> typeOf)
        {
            var stockAssembly = typeof(ScenarioModule).Assembly;
            var rank = new Dictionary<string, int>();
            foreach (var scenarioType in KSPScenarioType.GetAllScenarioTypesInAssemblies())
                if (!rank.ContainsKey(scenarioType.ModuleType.Name))
                    rank[scenarioType.ModuleType.Name] = rank.Count;

            var stock = new List<ScenarioEntry>();
            var mods = new List<ScenarioEntry>();
            var broken = new List<ScenarioEntry>();   // null entries: the load loop logs them
            foreach (var entry in entries)
            {
                if (entry == null) { broken.Add(null); continue; }
                var type = entry.ScenarioModule == null ? null : typeOf(entry.ScenarioModule);
                (type != null && type.Assembly == stockAssembly ? stock : mods).Add(entry);
            }

            // A stable sort: listed mods by KSP's order, unlisted ones after them as received.
            var received = new Dictionary<ScenarioEntry, int>();
            for (var i = 0; i < mods.Count; i++) received[mods[i]] = i;
            int Rank(ScenarioEntry e) => e.ScenarioModule != null && rank.TryGetValue(e.ScenarioModule, out var r) ? r : int.MaxValue;
            mods.Sort((a, b) => Rank(a) != Rank(b) ? Rank(a).CompareTo(Rank(b)) : received[a].CompareTo(received[b]));

            if (stock.Count > 0 && mods.Count > 0)
                LunaLog.Log($"[LMP]: [PiMP] Loading {stock.Count} stock scenario(s) before {mods.Count} mod scenario(s), in KSP's scenario type order");
            stock.AddRange(mods);
            stock.AddRange(broken);
            return stock;
        }
    }
}
