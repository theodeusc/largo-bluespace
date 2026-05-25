using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Glitchers.EcoKnow.Sandbox
{
    public class WinCondition
    {
        //This matches the states in ResultsWidget
        public enum Result {
            NOT_STARTED = 0,
            IN_RANGE = 1,
            STREAK = 2,
            GRACE = 3,
            FAILED = 4
        };
        private List<Result> _resultCache;
        public List<Result> Results => _resultCache;

        public string title { get; protected set; }
        public string description { get; protected set; }

        private int completionScore = 10; //{ get; protected set; }
        public int Score => Completed ? completionScore : 0;


        public enum TargetType {
            Entity,
            Item,
            Currency
        };
        protected int typeIndex;
        public int TypeIndex => typeIndex;
        public TargetType Type => (WinCondition.TargetType)typeIndex;

        protected int targetIndex;
        public int TargetIndex => targetIndex;

        public Entity TargetEntity
        {
            get
            {
                if (Type == TargetType.Entity)
                    return SandboxManager.Instance?.EntityManager?.GetEntityType(targetIndex);
                else
                    return null;
            }
        }
        public Item TargetItem
        {
            get
            {
                if (Type == TargetType.Item)
                    return SandboxManager.Instance?.PlayerInventory?.GetItemDef(targetIndex);
                else
                    return null;
            }
        }

        public float lowerLimit { get; protected set; }
        public float upperLimit { get; protected set; }

        private int _graceRemaining = 1;

        public int requiredRounds { get; protected set; }
        private int _consecutiveSuccesses = 0;
        public int ConsecutiveSuccesses => _consecutiveSuccesses;

        public bool FinalRoundOnly { get; private set; }

        // Number of trailing rounds in which a FinalRoundOnly condition is actually evaluated.
        // Two gives the player a penultimate-round "you're on track / running out of time"
        // signal plus the actual decider on the final round. Earlier rounds skip entirely so
        // mid-game pollution peaks can't lock in a permanent FAILED.
        private const int FINAL_ROUNDS_TO_EVALUATE = 2;

        private bool _completed;
        public bool Completed => IsCurrentlyComplete();

        public void Init(WinConditionRecord record)
        {
            //Setup from record

            title = record.Title;
            description = record.Description;
            typeIndex = record.TypeIndex;
            targetIndex = record.TargetIndex;

            lowerLimit = record.LowerLimit;
            upperLimit = record.UpperLimit;

            requiredRounds = record.RequiredRounds <= 0 ? 1 : record.RequiredRounds;
            _graceRemaining = 1;
            FinalRoundOnly = record.FinalRoundOnly;

            _resultCache = new List<Result>();
        }

        public void OnNewRound()
        {
            // FinalRoundOnly conditions only track during the final N rounds (penultimate +
            // final by default). Earlier rounds skip entirely so the cache stays empty and a
            // mid-game pollution peak can't push GRACE → FAILED and lock in defeat. Once the
            // tracking window opens, the standard cache + grace logic applies and IsCurrentlyComplete
            // looks at the latest result at EndGame to decide pass/fail.
            if (FinalRoundOnly)
            {
                SandboxManager mgr = SandboxManager.Instance;
                if (mgr == null) return;
                if (mgr.CurrentRound < mgr.MaxRounds - FINAL_ROUNDS_TO_EVALUATE) return;
            }

            if (GetLatestResult() == Result.FAILED)
            {
                //No longer tracked
            }
            else
            {
                bool success = HasConditionBeenMet();
                if (success)
                {
                    OnSuccessfulRound();
                }
                else
                {
                    OnFailedRound();
                }
            }
        }

        protected bool HasConditionBeenMet()
        {
            switch (Type)
            {
                case (WinCondition.TargetType.Entity):
                    {
                        return HasEntityConditionBeenMet();
                    }
                case (WinCondition.TargetType.Item):
                    {
                        if (TargetItem != null)
                            return HasItemConditionBeenMet(TargetItem.ID);
                        else
                            return false;
                    }
                case (WinCondition.TargetType.Currency):
                    {
                        return HasItemConditionBeenMet(PlayerInventory.CurrencyID);
                    }
                default:
                    return false;
            }
        }

        private bool HasEntityConditionBeenMet()
        {
            EntityManager entityManager = SandboxManager.Instance.EntityManager;
            if (entityManager != null)
            {
                Entity entityType = entityManager.GetEntityType(targetIndex);
                if (entityType != null)
                {
                    // Aggregate pollution wins ignore the per-entity bounds and instead pass
                    // when the catchment's worst-of-all-pollutants tier reads LOW. Keeps the
                    // win condition aligned with the single aggregator widget the player sees.
                    if (entityType.IsAggregatePollutionDisplay)
                    {
                        return PollutionTier.ComputeAggregateTier(entityManager) == PollutionTier.Low;
                    }

                    // Use the long-typed accessor — pollutant totals (eColi etc.) routinely sit
                    // above int.MaxValue and the int sibling saturates, breaking "≤ N" checks.
                    long totalPopulation = entityManager.GetTotalPopulationOfEntityTypeLong(targetIndex);
                    return IsWithinLimits(totalPopulation);
                }
            }

            return false;
        }

        private bool HasItemConditionBeenMet(string itemID)
        {
            PlayerInventory inventory = SandboxManager.Instance.PlayerInventory;
            if (inventory != null)
            {
                int quantity = inventory.GetAmountHeld(itemID);
                return IsWithinLimits(quantity); //int promotes to long for the comparison
            }

            return false;
        }

        // Single bracket helper used for both entity (long) and inventory (int promoted) checks.
        // Comparisons run in double so float thresholds like 5.0e13 compare cleanly against
        // long amounts (long → double is exact up to 2^53 ≈ 9e15).
        private bool IsWithinLimits(long amount)
        {
            double lower = lowerLimit;
            double upper = upperLimit;
            if ((upper > 0.0) && (upper >= lower))
            {
                return amount >= lower && amount <= upper;
            }
            //If the scenario designer has not set an upper limit or if the upper limit is smaller than the lower limit, we should not consider it
            return amount >= lower;
        }

        private void OnSuccessfulRound()
        {
            if (IsCurrentlyComplete())
            {
                PushResult(Result.STREAK);
            }
            else
            {
                PushResult(Result.IN_RANGE);
            }
        }

        private void OnFailedRound()
        {
            if (GetLatestResult() == Result.NOT_STARTED)
            {
                PushResult(Result.NOT_STARTED);
            }
            else
            {
                Result failResult = CanEnterGracePeriod() ? Result.GRACE : Result.FAILED;
                PushResult(failResult);
            }
        }


        #region Result
        private bool CanEnterGracePeriod()
        {
            return _graceRemaining > 0;
        }

        private bool IsCurrentlyComplete()
        {
            //Too early to start a STREAK
            int startIndex = _resultCache.Count - requiredRounds;
            if (startIndex < 0)
            {
                return false;
            }

            //If recentresults are all IN-RANGE or STREAK, we streak. otherwise we are in range
            Result[] recentResults = _resultCache.GetRange(startIndex, requiredRounds).ToArray();
            return recentResults.All(x => x == Result.IN_RANGE || x == Result.STREAK);
        }
        private void PushResult(Result result)
        {
            if (_resultCache == null)
            {
                _resultCache = new List<Result>();
            }

            if (result == Result.GRACE)
            {
                _graceRemaining -= 1;
            }

            _resultCache.Add(result);
        }

        public Result GetLatestResult()
        {
            if ((_resultCache == null) || (_resultCache.Count == 0))
            {
                return Result.NOT_STARTED;
            }

            return _resultCache.Last();
        }

        public Dictionary<int, string> GetRoundResults()
        {
            Dictionary<int, string> results = new Dictionary<int, string>();
            if (_resultCache != null)
            {
                for (int i = 0; i < _resultCache.Count; i++)
                {
                    //Match round number
                    results.Add(i + 1, _resultCache[i].ToString());
                }
            }

            return results;
        }
        #endregion
    }
}
