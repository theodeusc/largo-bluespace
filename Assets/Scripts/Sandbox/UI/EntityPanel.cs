using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class EntityPanel : MonoBehaviour
    {
        [SerializeField] private Transform _entityButtonContainer;
        [SerializeField] private EntityWidget _entityWidgetPrefab;

        private int _selectedEntityIndex = -1;
        public int SelectedEntityIndex => _selectedEntityIndex;

        // The visible panel background the entity rows sit in. This component's own
        // RectTransform is a full-height spacer (its LayoutElement takes all the leftover
        // vertical space in the side column) while the background hangs from the top and is
        // sized to its content — so anything that needs to sit flush under the entity list
        // must measure this, not the spacer. Derived from the container reference rather
        // than serialised separately so there is one wiring point, not two.
        public RectTransform PanelRect => _entityButtonContainer != null ? _entityButtonContainer.parent as RectTransform : null;

        public Action<int> onEntitySelected;
        public Action onEntityDeselected;

        private const string LogChannel = "[EntityPanel]";

        public void Init(Entity[] entities)
        {
            if (entities == null)
            {
                return;
            }

            SetupEntityList(entities);
        }

        public void Cleanup()
        {
            onEntitySelected = null;
            onEntityDeselected = null;
        }

        private void SetupEntityList(Entity[] entities)
        {
            if ((_entityWidgetPrefab == null) || (_entityButtonContainer == null))
            {
                return;
            }

            foreach (Transform child in _entityButtonContainer)
            {
                Destroy(child.gameObject);
            }

            for (int i = 0; i < entities.Length; i++)
            {
                int entityIndex = i;

                // Skip entities that opt out of the right-side widget list — e.g. pollutants
                // (incomprehensible raw counts, surfaced as an aggregate HIGH/MED/LOW badge
                // instead) and the invisible WaterTreatmentFacility marker. Distinct from
                // HiddenFromCellToken so biota with hidden cell-tokens (oyster / seal /
                // seagrass) still appear in the panel with their populations.
                if (entities[i] != null && entities[i].HiddenFromEntityPanel) continue;

                EntityWidget widget = Instantiate(_entityWidgetPrefab, _entityButtonContainer);
                if (widget != null)
                {
                    widget.SetEntity(entityIndex, entities[i]);
                }

                Button button = widget.Button;
                if (button != null)
                {
                    button.onClick.AddListener(delegate
                  {
                      if (_selectedEntityIndex == entityIndex)
                      {
                        //Deselect
                        DeselectEntity();
                      }
                      else
                      {
                        //Select
                        SelectEntity(entityIndex);
                      }
                  });
                }
            }

            if (this.GetComponent<RectTransform>() != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(this.GetComponent<RectTransform>());
            }
        }

        public EntityWidget GetWidgetForEntity(int index)
        {
            if (_entityButtonContainer != null)
            {
                EntityWidget[] widgets = _entityButtonContainer.GetComponentsInChildren<EntityWidget>();
                if ((widgets != null) && (widgets.Count() > 0))
                {
                    return widgets.FirstOrDefault(x => x.EntityIndex == index);
                }
            }

            return null;
        }

        private void UnfocusAllWidgets()
        {
            if (_entityButtonContainer != null)
            {
                foreach (EntityWidget widget in _entityButtonContainer.GetComponentsInChildren<EntityWidget>())
                {
                    widget.Unfocus();
                }
            }
        }

        public void UpdateAllWidgets()
        {
            if (_entityButtonContainer != null)
            {
                foreach (EntityWidget widget in _entityButtonContainer.GetComponentsInChildren<EntityWidget>())
                {
                    widget.UpdateQuantity();
                }
            }

            OrderWidgets();
        }

        private void OrderWidgets()
        {
            if (_entityButtonContainer != null)
            {
                EntityWidget[] widgets = _entityButtonContainer.GetComponentsInChildren<EntityWidget>();
                EntityManager entityManager = SandboxManager.Instance.EntityManager;
                if ((widgets != null) && (entityManager != null))
                {
                    List<Tuple<int, int>> populationsByIndex = new List<Tuple<int, int>>();
                    for (int i = 0; i < entityManager.EntityTypeCount; i++)
                    {
                        int population = entityManager.GetTotalPopulationOfEntityType(i);
                        populationsByIndex.Add(new Tuple<int, int>(i, population));
                    }

                    populationsByIndex = populationsByIndex.OrderByDescending(x => x.Item2).ThenBy(x => x.Item1).ToList();
                    for (int j = 0; j < populationsByIndex.Count; j++)
                    {
                        EntityWidget widget = widgets.FirstOrDefault(x => x.EntityIndex == populationsByIndex[j].Item1);
                        if (widget != null)
                        {
                            widget.transform.SetSiblingIndex(j);
                            //Debug.Log($"Entity Widget with ID {widget.EntityIndex} and Population {populationsByIndex[j].Item2} at Position {j}");
                        }
                    }
                }
            }
        }

        private void SelectEntity(int index)
        {
            //Clamp just in case?
            int maxIndex = 0;
            if (SandboxManager.Instance.EntityManager != null)
            {
                maxIndex = SandboxManager.Instance.EntityManager.EntityTypeCount;
            }

            _selectedEntityIndex = Mathf.Clamp(index, 0, maxIndex);

            //Now select our widget
            EntityWidget selected = GetWidgetForEntity(index);
            if (selected != null)
            {
                UnfocusAllWidgets();
                selected.Focus();

                onEntitySelected?.Invoke(index);
            }
            else
            {
                Debug.LogError($"{LogChannel} Failed to select widget for entity with index {index}");
            }
        }

        public void DeselectEntity()
        {
            _selectedEntityIndex = -1;
            UnfocusAllWidgets();
            onEntityDeselected?.Invoke();
        }

        public void OnEntityUpdated(int column, int row, int index)
        {
            EntityWidget selected = GetWidgetForEntity(index);
            if (selected != null)
            {
                selected.UpdateQuantity();
            }

            OrderWidgets();
        }
    }
}
