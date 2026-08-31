using System.Collections.Generic;
using System.Text.RegularExpressions;
using Glitchers.EcoKnow.Sandbox.Data;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    // Single point of access for per-item lookups into the loaded briefing sidecar.
    //
    // The briefing authors entity keys in PascalCase for the briefings website
    // (WaterPollution, Litter, Seal, Oyster) while the scenario JSON identifies entities
    // by snake_case ID (eColi_sewage, litter, seal, oyster) with an optional DisplayName
    // ("Water Pollution"). Nothing in the project bridged the two before. This class is
    // the ONLY place that knows the matching rules, so ToolPanel and any future consumer
    // don't each reinvent them.
    //
    // Every accessor is null-safe end to end: no briefing loaded (e.g. scene_Sandbox
    // entered directly in the editor, bypassing MenuScreen_Select) simply yields null and
    // callers hide their description UI, matching pre-briefing behaviour.
    public static class BriefingLookup
    {
        private static readonly Regex PascalCaseSplit = new Regex(@"(?<!^)(?=[A-Z])");

        private static BriefingData Briefing => ScenarioLoader.Instance != null ? ScenarioLoader.Instance.LoadedBriefing : null;

        // Returns the briefing blurb for an entity, or null when the briefing is absent or
        // has no entry for it. Matching is tried most-specific first:
        //   1. DisplayName with spaces stripped ("Water Pollution" -> "WaterPollution")
        //   2. Raw ID ("oyster" -> "Oyster")
        // both case-insensitively, so authors can key on whichever reads better.
        public static string GetEntityDescription(Entity entity)
        {
            if (entity == null) return null;

            Dictionary<string, string> descriptions = Briefing?.entity_descriptions;
            if (descriptions == null || descriptions.Count == 0) return null;

            if (!string.IsNullOrEmpty(entity.DisplayName))
            {
                string collapsed = entity.DisplayName.Replace(" ", string.Empty);
                string byDisplayName = FindValue(descriptions, collapsed);
                if (byDisplayName != null) return byDisplayName;
            }

            return FindValue(descriptions, entity.ID);
        }

        // Returns the briefing blurb stored under an arbitrary entity_descriptions key.
        // Used by HUD panels that aren't backed by an Entity record at all (e.g. the
        // currency panel, keyed "Currency"), so their copy is authored alongside the rest
        // of the briefing instead of being hardcoded in the UI.
        public static string GetDescriptionByKey(string key)
        {
            Dictionary<string, string> descriptions = Briefing?.entity_descriptions;
            if (descriptions == null || descriptions.Count == 0) return null;

            return FindValue(descriptions, key);
        }

        // Case-insensitive dictionary probe. The dictionary comes from Newtonsoft with the
        // default ordinal comparer, so we can't rely on TryGetValue alone for casing.
        private static string FindValue(Dictionary<string, string> source, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (source.TryGetValue(key, out string exact))
            {
                return string.IsNullOrWhiteSpace(exact) ? null : exact.Trim();
            }

            foreach (KeyValuePair<string, string> pair in source)
            {
                if (string.Equals(pair.Key, key, System.StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
                }
            }

            return null;
        }

        // Splits a PascalCase identifier into words ("WaterPollution" -> "Water Pollution").
        // Lives here rather than in BriefingFormatter so the composed modal text and any
        // per-entity UI derive their labels from one implementation.
        public static string Humanise(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            return PascalCaseSplit.Replace(id, " ");
        }
    }
}
