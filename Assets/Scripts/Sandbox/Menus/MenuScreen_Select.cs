using System.Collections.Generic;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Menus
{
    public class MenuScreen_Select : MenuScreen
    {
        [Header("Scenario Buttons")]
        [SerializeField] private Button_ScenarioSelect _scenarioSelectButtonPrefab;
        [SerializeField] private Transform _scenarioSelectContainer;

        private const string LogChannel = "[MenuScreen_Select]";

        // Only scenarios whose Scenario.Name matches an entry here are shown in the menu.
        // Other scenario JSONs remain in the project and can be re-enabled by editing this list.
        private static readonly HashSet<string> VisibleScenarioNames = new HashSet<string>
        {
            "Little Largo Blue Space"
        };

        protected override void OnEnabled()
        {
            base.OnEnabled();

            if (ScenarioLoader.Instance != null)
            {
                SetupScenarioList(ScenarioLoader.Instance.GetIntegratedScenarioConfigs());
            }
        }

        public void OnReturnPressed()
        {
            MainMenuController?.SetScreen(MainMenuController.Screen.MAIN);
        }

        public void OnLoadOtherScenarioPressed()
        {
            MainMenuController?.SetScreen(MainMenuController.Screen.LOAD_SCENARIO);
        }

        private void SetupScenarioList(List<ScenarioConfig> scenarioConfigs)
        {
            if ((scenarioConfigs == null) || (scenarioConfigs.Count <= 0))
            {
                Debug.LogError($"{LogChannel} Failed to setup Scenario List, config list is null or empty");
                return;
            }

            //Cleanup old stuff
            foreach (Transform child in _scenarioSelectContainer)
            {
                Destroy(child.gameObject);
            }

            //Add new buttons (filtered by VisibleScenarioNames)
            int displayIndex = 0;
            for (int i = 0; i < scenarioConfigs.Count; i++)
            {
                ScenarioConfig config = scenarioConfigs[i];
                if (config?.Scenario == null || !VisibleScenarioNames.Contains(config.Scenario.Name))
                {
                    continue;
                }
                displayIndex++;
                AddScenarioToList(displayIndex, config);
            }
        }

        private void AddScenarioToList(int index, ScenarioConfig config)
        {
            //Instantiate and set up button
            if (config != null)
            {
                Button_ScenarioSelect button = Instantiate(_scenarioSelectButtonPrefab, _scenarioSelectContainer);
                if (button != null)
                {
                    button.SetScenarioData(index, config.Scenario.Name, config.Scenario.Author, config.Scenario.ActionsPerRound);
                    button.PlayButton.onClick.AddListener(() =>
                    {
                        ScenarioLoader.Instance.SetLoadedConfig(config);
                        MainMenuController.SetScreen(MainMenuController.Screen.SETUP_SCENARIO);
                    });
                }
            }
        }
    }
}
