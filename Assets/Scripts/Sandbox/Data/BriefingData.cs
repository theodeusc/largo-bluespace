using System.Collections.Generic;

namespace Glitchers.EcoKnow.Sandbox.Data
{
    // Mirrors the EcoKnow scenario-briefings YAML schema
    // (https://ecoknowgames.github.io/briefings/). The canonical source is
    // <scenario>_briefing.yaml; the matching <scenario>_briefing.json is a
    // hand-converted runtime mirror, deserialised by ScenarioLoader.
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
