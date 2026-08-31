using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class ObjectivesPanel : MonoBehaviour
    {
        // Raised when an objective widget is tapped, carrying the condition it represents.
        // SandboxUI subscribes and opens the shared InfoPopup, which positions itself from
        // the pointer rather than from the widget.
        public event Action<WinCondition> onObjectiveSelected;

        [Header("Widgets")]
        [SerializeField] private ObjectiveWidget _widgetPrefab;
        [SerializeField] private Transform _widgetContainer;

        // Widgets spawned by the most recent Init. Tracked explicitly rather than re-queried
        // via GetComponentsInChildren: Destroy is deferred to end-of-frame, so a child query
        // made in the same frame as a rebuild would still return the outgoing widgets
        // alongside the new ones.
        private readonly List<ObjectiveWidget> _widgets = new List<ObjectiveWidget>();

        private const string LogChannel = "[ObjectivesPanel]";

        public void Init(WinCondition[] winConditions, EntityManager entityManager)
        {
            if ((_widgetPrefab == null) || (_widgetContainer == null))
            {
                Debug.LogError($"{LogChannel} Failed setup, Widget prefab or Container is null!");
                return;
            }

            //Remove old objectives
            foreach (ObjectiveWidget widget in _widgets)
            {
                if (widget == null) continue;
                widget.onClicked -= OnWidgetClicked;
                widget.Cleanup();
            }
            _widgets.Clear();

            foreach (Transform child in _widgetContainer)
            {
                Destroy(child.gameObject);
            }

            //Instantiate new ones
            foreach (WinCondition condition in winConditions)
            {
                ObjectiveWidget widget = Instantiate(_widgetPrefab, _widgetContainer);
                _widgets.Add(widget);

                widget.Init();
                widget.onClicked += OnWidgetClicked;

                widget.ResultsTracker.Init(null, -1, -1); //Not ideal but works for now

                switch ((WinCondition.TargetType)condition.TypeIndex)
                {
                    case (WinCondition.TargetType.Entity):
                        {
                            if (condition.TargetEntity != null)
                                widget.SetEntity(condition.TargetIndex, condition.TargetEntity);
                            break;
                        }
                    case (WinCondition.TargetType.Item):
                        {
                            if (condition.TargetItem != null)
                                widget.SetItem(condition.TargetIndex, condition.TargetItem);
                            break;
                        }
                    case (WinCondition.TargetType.Currency):
                        {
                            widget.SetCurrency();
                            break;
                        }
                    default:
                        {
                            Debug.LogError($"{LogChannel} Failed setup, Type not recognised!");
                            break;
                        }
                }
            }
        }

        public void UpdateWinConditions(int currentRound, int maxRounds)
        {
            if (_widgetContainer == null)
            {
                Debug.LogError($"{LogChannel} Failed to update Win Conditions, Widget Container is null!");
                return;
            }

            if (SandboxManager.Instance != null)
            {
                foreach(WinCondition condition in SandboxManager.Instance.WinConditions)
                {
                    ObjectiveWidget widget = GetWidgetByIndex(condition.Type, condition.TargetIndex);
                    if (widget != null)
                    {
                        widget.UpdateObjective(currentRound, maxRounds);
                    }
                }
            }
        }

        // Relays a widget tap upward with the condition it represents. Widgets whose
        // condition can't be resolved (shouldn't happen, but the lookup is nullable) are
        // dropped rather than raising an event with no payload.
        private void OnWidgetClicked(ObjectiveWidget widget)
        {
            if (widget == null) return;

            WinCondition condition = widget.Condition;
            if (condition == null)
            {
                Debug.LogWarning($"{LogChannel} Objective widget clicked but its WinCondition could not be resolved.");
                return;
            }

            onObjectiveSelected?.Invoke(condition);
        }

        private ObjectiveWidget GetWidgetByIndex(WinCondition.TargetType type, int index)
        {
            return _widgets.FirstOrDefault(x => x != null && x.Type == type && x.TargetIndex == index);
        }

        private ObjectiveWidget GetWidgetById(WinCondition.TargetType type, string id)
        {
            int index = -1;

            switch(type)
            {
                case (WinCondition.TargetType.Entity):
                    {
                        if (SandboxManager.Instance.EntityManager != null)
                            index = SandboxManager.Instance.EntityManager.GetEntityIndex(id);
                        break;
                    }
                case (WinCondition.TargetType.Item):
                    {
                        if (SandboxManager.Instance.PlayerInventory != null)
                            index = SandboxManager.Instance.PlayerInventory.GetItemIndex(id);
                        break;
                    }
                case (WinCondition.TargetType.Currency):
                    {
                        index = 0;
                        break;
                    }
                default:
                    return null;
            }

            //Try find widget
            if (index >= 0)
            {
                return GetWidgetByIndex(type, index);
            }

            return null;
        }

        public void OnEntityUpdated(int column, int row, int id)
        {
            ObjectiveWidget widget = GetWidgetByIndex(WinCondition.TargetType.Entity, id);
            if (widget != null)
            {
                widget.OnQuantityUpdated();
            }
        }

        public void OnInventoryUpdated(string id, int amount)
        {
            bool isCurrency = id.Equals(PlayerInventory.CurrencyID, StringComparison.OrdinalIgnoreCase);
            ObjectiveWidget widget = GetWidgetById(isCurrency ? WinCondition.TargetType.Currency : WinCondition.TargetType.Item, id);
            if (widget != null)
            {
                widget.OnQuantityUpdated();
            }
        }
    }
}
