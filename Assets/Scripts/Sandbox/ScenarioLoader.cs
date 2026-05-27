using UnityEngine;
using SimpleFileBrowser;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;
using Glitchers.EcoKnow.Sandbox.Data;

namespace Glitchers.EcoKnow.Sandbox
{
    public class ScenarioLoader : MonoBehaviour
    {
        #region Singleton
        protected static ScenarioLoader instance;
        public static ScenarioLoader Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = GetInstance();

                    if (instance == null)
                    {
                        Debug.LogError("An instance of " + typeof(ScenarioLoader) +
                            " is needed in the scene, but there is none.");
                    }
                }

                return instance;
            }
        }

        public static bool Exists
        {
            get
            {
                return instance;
            }
        }

        private static ScenarioLoader GetInstance()
        {
            if (instance == null)
            {
                return FindFirstObjectByType<ScenarioLoader>();
            }
            return instance;
        }
        #endregion

        [Header("Scenario")]
        [SerializeField] private ScenarioConfigDataList _integratedScenarioList;
        [SerializeField] private ScenarioNodeGraph _activeScenarioGraph;
        private ScenarioConfig _loadedConfig;
        public ScenarioConfig LoadedConfig => _loadedConfig;
        public Scenario LastPlayedScenario => _loadedConfig == null ? null : _loadedConfig.Scenario;

        // Briefing sidecars (<scenario>_briefing.json) keyed by Scenario.Name.
        // Populated as scenarios are discovered; looked up by SandboxUI when the
        // ObjectivesModal needs richer intro copy than scenario.Description.
        private readonly Dictionary<string, BriefingData> _briefingsByScenarioName = new Dictionary<string, BriefingData>();
        public BriefingData LoadedBriefing
        {
            get
            {
                if (_loadedConfig == null || _loadedConfig.Scenario == null) return null;
                return _briefingsByScenarioName.TryGetValue(_loadedConfig.Scenario.Name, out BriefingData briefing) ? briefing : null;
            }
        }

        private const string LogChannel = "[ScenarioLoader]";

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        #region Loading Scenario Config
        public static void ShowLoadDialog(Action<Scenario> onSuccess, Action onCancel)
        {
            FileBrowser.SetFilters(false, ".json");
            FileBrowser.ShowLoadDialog(
            (filePaths) =>
            {
                //On file chosen
                string rawJson = ParseJsonFromFile(filePaths);
                if (!string.IsNullOrEmpty(rawJson))
                {
                    ScenarioConfig config = LoadConfig(rawJson);
                    if (config != null)
                    {
                        Instance._loadedConfig = config;
                        // Sibling sidecar: <picked-file>_briefing.json next to the chosen scenario.
                        string pickedPath = (filePaths != null && filePaths.Length > 0) ? filePaths[0] : null;
                        Instance.TryLoadSidecarBriefingFromFile(pickedPath, config.Scenario?.Name);
                        onSuccess?.Invoke(config.Scenario);
                    }
                }
            },
            () =>
            {
                //On cancelled
                onCancel?.Invoke();
            },
            FileBrowser.PickMode.Files,
            false,
            null,
            null,
            "Load Scenario JSON File"
            );
        }

        public static void HideLoadDialog()
        {
            FileBrowser.HideDialog();
        }

        private static string ParseJsonFromFile(string[] filePaths)
        {
            if ((filePaths != null) && (filePaths.Length > 0))
            {
                //Ignore multi-select
                string filePath = filePaths[0];

                //Read from file
                string text = System.IO.File.ReadAllText(filePath);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }

            Debug.LogError($"{LogChannel} Failed to parse Json, either filePath was invalid or the selected file was empty");
            return null;
        }

        //Load ScenarioConfig from raw json
        public static ScenarioConfig LoadConfig(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"{LogChannel} Failed to load ScenarioConfig from JSON, JSON is null or empty");
                return null;
            }

            //Deserialise from JSON
            List<string> errors = new List<string>();
            ScenarioConfig config = JsonConvert.DeserializeObject<ScenarioConfig>(json,
                new JsonSerializerSettings
                {
                    Error = (sender, args) =>
                    {
                        errors.Add(args.ErrorContext.Error.Message);
                        args.ErrorContext.Handled = true;
                    }
                });

            //Check and print errors, if any
            if (errors.Count > 0)
            {
                foreach (string error in errors)
                {
                    Debug.Log($"{LogChannel} Failed to deserialise ScenarioConfig from JSON: {error}");
                }
            }

            if ((config != null) && (config.Scenario != null))
            {
                //Validation
                if (Application.version != config.AppVersion)
                {
                    Debug.LogWarning($"{LogChannel} Scenario {config.Scenario.Name} was exported from a different version of the Application {config.AppVersion} (Current: {Application.version}). Scenario may not work as intended");
                }
#if UNITY_EDITOR
                if (Application.unityVersion != config.UnityVersion)
                {
                    Debug.LogWarning($"{LogChannel} Scenario {config.Scenario.Name} was exported from a different version of the Unity Editor {config.UnityVersion} (Current: {Application.unityVersion}). Scenario may not work as intended");
                }
#endif

                if (config.Scenario == null)
                {
                    Debug.LogError($"{LogChannel} Failed to load ScenarioConfig from JSON. Scenario is null!");
                    return null;
                }
                else
                {
                    if ((config.Scenario.Map == null) || (config.Scenario.Map.gridDef == null) || (config.Scenario.Map.gridDef.tileIDs == null)) //Check for tileIDs as well
                    {
                        Debug.LogError($"{LogChannel} Failed to load ScenarioConfig from JSON. Map Layout is null! Scenario must have a valid Map Layout");
                        return null;
                    }

                    if (config.Scenario.Matrix == null && config.Scenario.Matrices == null)
                    {
                        Debug.LogError($"{LogChannel} Failed to load ScenarioConfig from JSON. Matrix is null! Scenario must have a valid Matrix or Matrices array");
                        return null;
                    }

                    if (config.Scenario.Entities == null || config.Scenario.Entities.Count() <= 0)
                    {
                        Debug.LogError($"{LogChannel} Failed to load ScenarioConfig from JSON. No Entities found in the JSON file. Scenario must have valid Entities");
                        return null;
                    }

                    if (config.Scenario.WinConditions == null || config.Scenario.WinConditions.Count() <= 0)
                    {
                        Debug.LogError($"{LogChannel} Failed to load ScenarioConfig from JSON. No Win Conditions found in the JSON file. Scenario must have at least one valid Win Condition");
                        return null;
                    }

                    if (config.Scenario.Rounds <= 0)
                    {
                        Debug.LogWarning($"{LogChannel} Invalid number of rounds ({config.Scenario.Rounds}) found in Scenario {config.Scenario.Name}. Using default value");
                    }

                    if (config.Scenario.ActionsPerRound <= 0)
                    {
                        Debug.LogWarning($"{LogChannel} Invalid number of actions per round ({config.Scenario.Rounds}) found in Scenario {config.Scenario.Name}. Using default value");
                    }
                }

                //If we have reached this point we are valid
            }
            else
            {
                Debug.LogError($"{LogChannel} Failed to load ScenarioConfig from JSON. Likely an error with parsing/deserialisation");
            }

            return config;
        }

        public void SetLoadedConfig(ScenarioConfig config)
        {
            if (config != null)
            {
                _loadedConfig = config;
            }
        }
        #endregion

        #region Integrated Scenarios
        public ScenarioConfigDataList GetIntegratedScenarioList()
        {
            return _integratedScenarioList;
        }

        public List<ScenarioConfig> GetIntegratedScenarioConfigs()
        {
            List<ScenarioConfig> _scenarioConfigs = new List<ScenarioConfig>();

            if (_integratedScenarioList != null)
            {
                foreach (ScenarioConfigDataList.ScenarioAsset asset in _integratedScenarioList.ScenarioAssets)
                {
                    if (asset.jsonAsset != null)
                    {
                        string rawJson = asset.jsonAsset.text;
                        if (!string.IsNullOrEmpty(rawJson))
                        {
                            ScenarioConfig config = LoadConfig(rawJson);
                            if (config != null)
                            {
                                _scenarioConfigs.Add(config);
                                // Sidecar briefing: <asset-name>_briefing under the same
                                // Resources subfolder. Keyed by Scenario.Name so SandboxUI
                                // can look it up at modal-init time.
                                TryLoadSidecarBriefingFromResources(asset.jsonAsset.name, config.Scenario?.Name);
                            }
                        }
                    }
                }
            }

            return _scenarioConfigs;
        }

        // Loads <basename>_briefing.json from the Resources/Scenarios folder and
        // stores it against scenarioName. Silent no-op if the file is missing,
        // empty, or fails to deserialise.
        private void TryLoadSidecarBriefingFromResources(string assetBasename, string scenarioName)
        {
            if (string.IsNullOrEmpty(assetBasename) || string.IsNullOrEmpty(scenarioName)) return;
            try
            {
                TextAsset briefingAsset = Resources.Load<TextAsset>($"Scenarios/{assetBasename}_briefing");
                if (briefingAsset == null || string.IsNullOrEmpty(briefingAsset.text)) return;
                BriefingData briefing = JsonConvert.DeserializeObject<BriefingData>(briefingAsset.text);
                if (briefing != null)
                {
                    _briefingsByScenarioName[scenarioName] = briefing;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogChannel} Failed to load sidecar briefing for {assetBasename}: {ex.Message}");
            }
        }

        // Loads a sibling <basename>_briefing.json next to a scenario file picked
        // via the load dialog. Same silent-fail policy as the Resources path.
        private void TryLoadSidecarBriefingFromFile(string scenarioFilePath, string scenarioName)
        {
            if (string.IsNullOrEmpty(scenarioFilePath) || string.IsNullOrEmpty(scenarioName)) return;
            try
            {
                string dir = System.IO.Path.GetDirectoryName(scenarioFilePath);
                string basename = System.IO.Path.GetFileNameWithoutExtension(scenarioFilePath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(basename)) return;
                string briefingPath = System.IO.Path.Combine(dir, $"{basename}_briefing.json");
                if (!System.IO.File.Exists(briefingPath)) return;
                BriefingData briefing = JsonConvert.DeserializeObject<BriefingData>(System.IO.File.ReadAllText(briefingPath));
                if (briefing != null)
                {
                    _briefingsByScenarioName[scenarioName] = briefing;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogChannel} Failed to load sidecar briefing from {scenarioFilePath}: {ex.Message}");
            }
        }
        #endregion

        #region Load Sandbox
#if UNITY_EDITOR
        public ScenarioConfig GetCurrentGraphConfig()
        {
            if (_activeScenarioGraph != null)
            {
                return _activeScenarioGraph.GetConfig();
            }

            return null;
        }

        public void RequestStartActiveGraph()
        {
            ScenarioConfig config = GetCurrentGraphConfig();
            if (config != null)
            {
                _loadedConfig = config;
                StartLoadedConfig();
            }
        }
#endif
        public void RequestStartLoadedConfig()
        {
            StartLoadedConfig();
        }

        private void StartLoadedConfig()
        {
            if ((_loadedConfig != null) && (SandboxManager.Instance != null))
            {
                SandboxManager.Instance.StartNewGame(_loadedConfig.Scenario);
            }
        }

        #endregion
    }
}
