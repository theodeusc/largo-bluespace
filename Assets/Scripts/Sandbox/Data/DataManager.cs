using System;
using System.Collections.Generic;
using System.Linq;
using Glitchers.EcoKnow.Sandbox.Grid;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SimpleFileBrowser;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.Data
{
    public class DataManager : MonoBehaviour
    {
        #region Singleton
        protected static DataManager instance;
        public static DataManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = GetInstance();

                    if (instance == null)
                    {
                        Debug.LogError("An instance of " + typeof(DataManager) +
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

        private static DataManager GetInstance()
        {
            if (instance == null)
            {
                return FindFirstObjectByType<DataManager>();
            }
            return instance;
        }
        #endregion

        private List<EventDataObject> _eventLog;
        public int RecordedEventCount => _eventLog.Count;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.P))
            {
                ExportData();
            }
        }

        public void RecordEvent(EventType type)
        {
            if (_eventLog == null)
            {
                _eventLog = new List<EventDataObject>();
            }

            Dictionary<string, object> metaData = new Dictionary<string, object>();

            /*if (type == EventType.SELL
                || type == EventType.INTRODUCE
                || type == EventType.HARVEST)
            {
                Dictionary<string, int> inventoryDiff = GetInventoryDifference(GetInventory());
                if (inventoryDiff != null)
                {
                    //metaData.Add("InventoryChange", inventoryDiff);
                }
            }*/

            //Gather data here
            EventDataObject ev = new EventDataObject(
                type,
                GetPlayer(),
                GetRound(),
                GetAction(),
                GetPopulations(),
                GetInventory(),
                GetWinConditions(),
                GetMapPopulations(),
                metaData// != null ? metaData.ToDictionary(x => x.Key, y => y.Object) : null
                );


            if (ev != null)
            {
                _eventLog.Add(ev);
            }
        }

        public void ClearData()
        {
            _eventLog?.Clear();
        }

        #region Export Data
        private void ExportData(string filePath = null)
        {
            GameDataObject data = new GameDataObject
                (
                    ScenarioLoader.Instance.LoadedConfig,
                    GetCalculator(),
                    _eventLog.ToArray()
                );

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            if (!string.IsNullOrEmpty(json))
            {
                string path = filePath;
                if (path == null)
                {
                    string fileName = data.Config.Scenario.Name.Replace(" ", "_");
                    path = Application.persistentDataPath + "/" + fileName + "_data.json";
                }

                System.IO.File.WriteAllText(path, json);
                Debug.Log("Data Exported!");
            }
        }

        public void ShowSaveDialog(Action onSuccess, Action onCancel)
        {
#if !(UNITY_STANDALONE || UNITY_EDITOR)
            // Save-to-arbitrary-path is unavailable under mobile scoped storage.
            Debug.LogWarning("[DataManager] Export-to-file is not available on this platform.");
            onCancel?.Invoke();
            return;
#else
            //Get scenario name
            string scenarioName = "Unknown";
            if ((ScenarioLoader.Instance != null) && (ScenarioLoader.Instance.LastPlayedScenario != null))
            {
                scenarioName = ScenarioLoader.Instance.LastPlayedScenario.Name;
            }

            scenarioName = scenarioName.Replace(" ", "_");
            string defaultFileName = string.Format($"{scenarioName}_data.json");

            //Show dialog
            FileBrowser.SetFilters(false, ".json");
            FileBrowser.ShowSaveDialog(
            (filePaths) =>
            {
                if ((filePaths != null) && (filePaths.Length > 0))
                {
                    //On file saved
                    string filePath = filePaths[0];
                    ExportData(filePath);
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
            defaultFileName,
            "Export Game Data"
            );
#endif
        }
        #endregion

        #region Data Gathering
        private CalculatorDataObject GetCalculator()
        {
            if (SandboxManager.Instance != null)
            {
                EntityManager entityManager = SandboxManager.Instance.EntityManager;
                if ((entityManager != null) && (entityManager.Calculator != null))
                {
                    return new CalculatorDataObject(
                        entityManager.Calculator.Name(),
                        entityManager.Calculator.Version()
                        );
                }
            }

            return null;
        }

        private List<CellDataObject> GetMapPopulations()
        {
            List<CellDataObject> mapData = new List<CellDataObject>();

            //Write the entire map + co-ordinates and populations to an object
            GridManager gridManager = SandboxManager.Instance.GridManager;
            EntityManager entityManager = SandboxManager.Instance.EntityManager;
            if ((gridManager != null) && (entityManager != null))
            {
                Vector2 gridSize = gridManager.GridSize;
                for (int column = 0; column < gridSize.x; column++)
                {
                    for (int row = 0; row < gridSize.y; row++)
                    {
                        mapData.Add(new CellDataObject(column, row, entityManager.GetPopulationsInCell(column, row)));
                    }
                }
            }

            return mapData;
        }

        private Dictionary<string, int> GetPopulations()
        {
            Dictionary<string, int> populationList = new Dictionary<string, int>();

            if (SandboxManager.Instance.EntityManager != null)
            {
                Entity[] entityList = SandboxManager.Instance.EntityManager.GetEntityTypeList();
                if (entityList != null)
                {
                    for (int i = 0; i < entityList.Length; i++)
                    {
                        Entity entityType = entityList[i];
                        if (entityType != null)
                        {
                            int total = SandboxManager.Instance.EntityManager.GetTotalPopulationOfEntityType(i);
                            populationList.Add(entityType.ID, total);
                        }
                    }
                }
            }

            return populationList;
        }

        private Dictionary<string, int> GetInventory()
        {
            if (SandboxManager.Instance.PlayerInventory != null)
            {
                return new Dictionary<string, int>(SandboxManager.Instance.PlayerInventory.Inventory);
            }

            return null;
        }

        private List<WinConditionDataObject> GetWinConditions()
        {
            List<WinConditionDataObject> winConditionList = new List<WinConditionDataObject>();

            for (int i = 0; i < SandboxManager.Instance.WinConditions.Count; i++)
            {
                WinCondition winCondition = SandboxManager.Instance.WinConditions[i];
                WinConditionDataObject data = new WinConditionDataObject
                    (
                        winCondition.title,
                        winCondition.Completed,
                        winCondition.GetRoundResults()
                    );

                winConditionList.Add(data);
            }

            return winConditionList;
        }

        private int GetPlayer()
        {
            if (MultiplayerManager.Instance != null)
            {
                return MultiplayerManager.Instance.CurrentPlayerIndex + 1; //Account for 0
            }

            return 1;
        }

        private int GetRound()
        {
            if (SandboxManager.Instance != null)
            {
                return SandboxManager.Instance.CurrentRound + 1; //Account for 0
            }

            return -1;
        }

        private int GetAction()
        {
            return SandboxManager.GetMaxActionPoints() - SandboxManager.GetAvailableActionPoints();
        }
        #endregion

        #region Data Fetch
        public List<EventDataObject> FetchDataPoints(EventType[] types)
        {
            List<EventDataObject> eventObjects = new List<EventDataObject>();

            foreach(EventDataObject evt in _eventLog)
            {
                if (types.Contains(evt.Type))
                {
                    eventObjects.Add(evt);
                }
            }

            return eventObjects;
        }
        #endregion

        #region Data Comparison
        private Dictionary<string, int> GetInventoryDifference(Dictionary<string, int> current)
        {
            if (RecordedEventCount < 1)
            {
                return current;
            }

            Dictionary<string, int> previousInventory = _eventLog[RecordedEventCount - 1].Inventory;
            if (previousInventory != null)
            {
                Dictionary<string, int> inventoryDiff = new Dictionary<string, int>(previousInventory);
                foreach(KeyValuePair<string, int> item in current)
                {
                    if (previousInventory.ContainsKey(item.Key))
                    {
                        inventoryDiff[item.Key] = current[item.Key] - previousInventory[item.Key];
                        if (inventoryDiff[item.Key] == 0)
                        {
                            inventoryDiff.Remove(item.Key);
                        }
                    }
                    else
                    {
                        inventoryDiff.Remove(item.Key);
                    }
                }

                return inventoryDiff;
            }

            return null;
        }

        #endregion
    }
}

