using System.Linq;
using TMPro;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class ObjectiveWidget : MonoBehaviour
    {
        private CanvasGroup _canvasGroup => this.GetComponent<CanvasGroup>();

        [Header("Entity")]
        [SerializeField] private EntityIcon _entityIcon;

        [Header("Round Target")]
        [SerializeField] private TMP_Text _roundTargetText;
        [SerializeField] private GameObject _roundTargetCheck;

        [Header("Results Tracker")]
        [SerializeField] private ResultsTracker _resultsTracker;
        public ResultsTracker ResultsTracker => _resultsTracker;

        private WinCondition.TargetType _type;
        public WinCondition.TargetType Type => _type;

        private int _targetIndex; //Safety
        public int TargetIndex => _targetIndex;

        private const string LogChannel = "[ObjectiveWidget]";

        public void SetEntity(int index, Entity entity)
        {
            if (entity == null)
            {
                Debug.LogError($"{LogChannel} Failed setup, Entity is null!");
                return;
            }

            _targetIndex = index;
            _type = WinCondition.TargetType.Entity;

            if (_entityIcon != null)
            {
                _entityIcon.SetEntity(entity);
            }
        }

        public void SetItem(int index, Item itemDef)
        {
            if (itemDef == null)
            {
                Debug.LogError($"{LogChannel} Failed setup, Item is null!");
                return;
            }

            _targetIndex = index;
            _type = WinCondition.TargetType.Item;

            if (_entityIcon != null)
            {
                _entityIcon.SetItem(itemDef);
            }
        }

        public void SetCurrency()
        {
            if (_entityIcon != null)
            {
                _entityIcon.SetCurrency();
            }

            _targetIndex = 0;
            _type = WinCondition.TargetType.Currency;
        }

        public void UpdateObjective(int currentRound, int maxRound)
        {
            WinCondition condition = GetWinCondition();
            if (condition != null)
            {
                if (condition.Completed)
                {
                    //Set Complete
                    SetComplete();
                }
                else
                {
                    //Check for failure
                    if ((condition.Results != null) && (condition.Results.Count > 0))
                    {
                        if (condition.Results.Last() == WinCondition.Result.FAILED)
                        {
                            SetFailed();
                        }
                        else
                        {
                            SetInProgress(condition.requiredRounds);
                        }
                    }
                    else
                    {
                        SetInProgress(condition.requiredRounds);
                    }
                }

                if ((condition.Results != null) && (condition.Results.Count > 0))
                {
                    _resultsTracker?.UpdateResultsTrack(condition.Results, currentRound, maxRound);
                }

                UpdateQuantity(condition);
            }
        }

        public void OnQuantityUpdated()
        {
            WinCondition condition = GetWinCondition();
            if (condition != null)
            {
                UpdateQuantity(condition);
            }
        }

        public void UpdateQuantity(WinCondition condition)
        {
            if (condition.GetLatestResult() == WinCondition.Result.FAILED)
            {
                return;
            }

            // Aggregate pollution win condition: shows worst-of-all-pollutants tier and is
            // satisfied when the aggregate reads LOW. Matches the aggregator EntityWidget so
            // the two readouts can never disagree.
            EntityManager em = SandboxManager.Instance.EntityManager;
            if (_type == WinCondition.TargetType.Entity && em != null)
            {
                Entity entity = em.GetEntityType(TargetIndex);
                if (entity != null && entity.IsAggregatePollutionDisplay)
                {
                    string aggregate = PollutionTier.ComputeAggregateTier(em);
                    _resultsTracker?.UpdateActiveWidgetTier(aggregate, PollutionTier.Colour(aggregate));
                    return;
                }
                if (entity != null && entity.DisplayAsPollutionTier)
                {
                    long pop = em.GetTotalPopulationOfEntityTypeLong(TargetIndex);
                    string tier = PollutionTier.Classify(pop, entity);
                    _resultsTracker?.UpdateActiveWidgetTier(tier, PollutionTier.Colour(tier));
                    return;
                }
            }

            int quantity = 0;
            switch (_type)
            {
                case (WinCondition.TargetType.Entity):
                    {
                        if (em != null)
                        {
                            quantity = em.GetTotalPopulationOfEntityType(TargetIndex);
                        }
                        break;
                    }
                case (WinCondition.TargetType.Item):
                    {
                        if (SandboxManager.Instance.PlayerInventory != null)
                        {
                            Item itemDef = SandboxManager.Instance.PlayerInventory.GetItemDef(TargetIndex);
                            if (itemDef != null)
                            {
                                quantity = SandboxManager.Instance.PlayerInventory.GetAmountHeld(itemDef.ID);
                            }
                        }
                        break;
                    }
                case (WinCondition.TargetType.Currency):
                    {
                        if (SandboxManager.Instance.PlayerInventory != null)
                        {
                            quantity = SandboxManager.Instance.PlayerInventory.GetAmountHeld(PlayerInventory.CurrencyID);
                        }
                        break;
                    }
                default:
                    break;
            }

            _resultsTracker?.UpdateActiveWidget(quantity, condition.lowerLimit, condition.upperLimit);
        }


        private WinCondition GetWinCondition()
        {
            if (SandboxManager.Instance.WinConditions != null)
            {
                switch (_type)
                {
                    case (WinCondition.TargetType.Entity):
                        return SandboxManager.Instance.WinConditions.FirstOrDefault(x => x.TypeIndex == (int)WinCondition.TargetType.Entity && x.TargetIndex == TargetIndex);
                    case (WinCondition.TargetType.Item):
                        return SandboxManager.Instance.WinConditions.FirstOrDefault(x => x.TypeIndex == (int)WinCondition.TargetType.Item && x.TargetIndex == TargetIndex);
                    case (WinCondition.TargetType.Currency):
                        return SandboxManager.Instance.WinConditions.FirstOrDefault(x => x.TypeIndex == (int)WinCondition.TargetType.Currency);
                    default:
                        return null;
                }
            }

            return null;
        }


        #region Overall State
        private void SetComplete()
        {
            if (_roundTargetCheck != null)
            {
                _roundTargetCheck.SetActive(true);
            }
        }

        private void SetInProgress(int rounds)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
            }

            if (_roundTargetText != null)
            {
                _roundTargetText.text = rounds.ToString();
            }

            if (_roundTargetCheck != null)
            {
                _roundTargetCheck.SetActive(false);
            }
        }

        private void SetFailed()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0.4f;
            }

            if (_roundTargetText != null)
            {
                _roundTargetText.text = "FAIL";
            }

            if (_roundTargetCheck != null)
            {
                _roundTargetCheck.SetActive(false);
            }
        }
        #endregion
    }
}
