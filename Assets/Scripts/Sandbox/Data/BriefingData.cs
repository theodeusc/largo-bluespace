using System.Collections.Generic;

namespace Glitchers.EcoKnow.Sandbox.Data
{
    // Mirrors the EcoKnow scenario-briefings YAML schema
    // (https://ecoknowgames.github.io/briefings/). The canonical source is
    // Assets/_EcoKnow/Briefings/<scenario>_briefing.yaml; the matching
    // Assets/_EcoKnow/Resources/Scenarios/<scenario>_briefing.json is a
    // hand-converted runtime mirror, deserialised by ScenarioLoader.
    //
    // The YAML deliberately lives OUTSIDE Resources/. Unity imports .yaml as a
    // TextAsset too, so keeping both files in Resources/Scenarios made
    // Resources.Load<TextAsset>("Scenarios/<scenario>_briefing") ambiguous — if it
    // resolved to the YAML, JsonConvert threw and the briefing silently vanished.
    [System.Serializable]
    public class BriefingData
    {
        public string situation;
        public string gameplay_summary;
        public InfoCard info_card;
        public string actions_intro;
        public Dictionary<string, string> entity_descriptions;
        public List<Objective> objectives;
        public List<WatchOut> watch_out;
        public string why_this_matters;
        public List<FurtherReading> further_reading;

        [System.Serializable]
        public class InfoCard
        {
            public string title;
            public string image;
            public string body;
        }

        [System.Serializable]
        public class Objective
        {
            public string title;
            public string entity;
            public string icon;
            public string description;
            public string goal;
        }

        [System.Serializable]
        public class WatchOut
        {
            public string entity;
            public string icon;
            public string name;
            public string description;
        }

        [System.Serializable]
        public class FurtherReading
        {
            public string title;
            public string description;
            public string url;
        }
    }
}
