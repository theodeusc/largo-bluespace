using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class EntityWidget : MonoBehaviour
    {
        public enum State { DEFAULT, FOCUSED };
        private State _currentState;

        private int _entityIndex; //Safety for selection/deselection, as we re-order the list
        public int EntityIndex => _entityIndex;

        [SerializeField] private Button _button;
        public Button Button => _button;

        [Header("UI Elements")]
        [SerializeField] private Image _entityIcon;
        [SerializeField] private Image _entityBackground;
        [SerializeField] private Image _quantityBackground;
        [SerializeField] private TMP_Text _quantityText;

        [Header("Colours")]
        [SerializeField] private Color _defaultColour;
        [SerializeField] private Color _focusedColour;

        private const string LogChannel = "[EntityWidget]";

        #region Visuals
        public void SetEntity(int index, Entity entity)
        {
            if (entity == null)
            {
                Debug.LogError($"{LogChannel} Failed setup, Entity is null!");
                return;
            }

            _entityIndex = index;

            //Set icon — clear any prefab-baked tint (the legacy design used a black tint to
            //render the high-contrast emoji icons as silhouettes; current pixel-art icons
            //carry their own colours and should render unmodulated).
            if (_entityIcon != null)
            {
                Sprite resource = Resources.Load<Sprite>(entity.Icon);
                if (resource != null)
                {
                    _entityIcon.sprite = resource;
                    _entityIcon.color = Color.white;
                }
                else
                {
                    Debug.LogError($"{LogChannel} Failed to find icon for entity at path {entity.Icon}!");
                }
            }

            //Set Colour — icon backplate is always white; the entity's Colour field is still
            //used elsewhere (cell tokens) but the icon panel reads cleaner with a uniform
            //white plate so each entity's pixel-art icon shows its own colours unmuted.
            if (_entityBackground != null)
            {
                _entityBackground.color = Color.white;
            }

            //Update populations
            UpdateQuantity();
        }

        public void UpdateQuantity()
        {
            EntityManager em = SandboxManager.Instance.EntityManager;
            if (em == null || _quantityText == null) return;

            Entity entity = em.GetEntityType(_entityIndex);

            // Aggregator: a single visible entity stands in for the whole catchment's
            // pollution — its tier is the worst-of across every DisplayAsPollutionTier
            // entity in the scenario. Other pollutants are hidden via HiddenFromEntityPanel
            // so the player sees one unified "Pollution: LOW/MED/HIGH" widget.
            if (entity != null && entity.IsAggregatePollutionDisplay)
            {
                string aggregate = PollutionTier.ComputeAggregateTier(em);
                _quantityText.text = aggregate;
                _quantityText.color = PollutionTier.Colour(aggregate);
                return;
            }

            // Pollutant entities show LOW/MED/HIGH (coloured) instead of a raw number — raw
            // pollutant totals routinely exceed int.MaxValue (eColi ~10^14) and would clamp
            // to a static-looking 2.1B reading even as the simulation changes underneath.
            if (entity != null && entity.DisplayAsPollutionTier)
            {
                long pop = em.GetTotalPopulationOfEntityTypeLong(_entityIndex);
                string tier = PollutionTier.Classify(pop, entity);
                _quantityText.text = tier ?? "?";
                _quantityText.color = PollutionTier.Colour(tier);
                return;
            }

            int population = em.GetTotalPopulationOfEntityType(_entityIndex);
            _quantityText.text = FormatQuantity(population);
            _quantityText.color = Color.white;
        }

        private string FormatQuantity(int quantity)
        {
            if (quantity >= 1000000)
            {
                float roundedQuantity = Mathf.Floor(((float)quantity / 100000f) * 10f) / 10f;
                return roundedQuantity.ToString("0.#") + "M";
            }
            else if (quantity >= 1000)
            {
                float roundedQuantity = Mathf.Floor(((float)quantity / 1000f) * 10f) / 10f;
                return roundedQuantity.ToString("0.#") + "k";
            }

            return quantity.ToString();
        }
        #endregion

        #region State
        public void Focus()
        {
            SetState(State.FOCUSED);
        }

        public void Unfocus()
        {
            SetState(State.DEFAULT);
        }

        private void SetState(State state)
        {
            _currentState = state;
            switch (state)
            {
                case (State.FOCUSED):
                    {
                        if (_quantityBackground != null)
                        {
                            _quantityBackground.color = _focusedColour;
                        }
                        break;
                    }
                case (State.DEFAULT):
                default:
                    {
                        if (_quantityBackground != null)
                        {
                            _quantityBackground.color = _defaultColour;
                        }
                        break;
                    }
            }
        }
        #endregion
    }
}
