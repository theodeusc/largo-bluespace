using System.Collections.Generic;

namespace Glitchers.EcoKnow.Sandbox
{
    // Defeat-side mirror of WinCondition. Same TargetType evaluator (Entity / Item / Currency),
    // same [LowerLimit, UpperLimit] semantics from IsWithinLimits, but without the streak /
    // grace / multi-round accumulation: the moment IsMet returns true at the end of a round,
    // SandboxManager short-circuits to EndGame(Result.LOSE).
    public class LoseCondition
    {
        public string Title { get; private set; }
        public string Description { get; private set; }

        public WinCondition.TargetType Type => (WinCondition.TargetType)_typeIndex;
        public int TypeIndex => _typeIndex;
        public int TargetIndex => _targetIndex;
        public float LowerLimit { get; private set; }
        public float UpperLimit { get; private set; }
        public bool FinalRoundOnly { get; private set; }
        public string TargetItemID { get; private set; }

        private int _typeIndex;
        private int _targetIndex;

        public Entity TargetEntity =>
            Type == WinCondition.TargetType.Entity
                ? SandboxManager.Instance?.EntityManager?.GetEntityType(_targetIndex)
                : null;

        public Item TargetItem =>
            Type == WinCondition.TargetType.Item
                ? SandboxManager.Instance?.PlayerInventory?.GetItemDef(_targetIndex)
                : null;

        public void Init(LoseConditionRecord record)
        {
            Title = record.Title;
            Description = record.Description;
            _typeIndex = record.TypeIndex;
            _targetIndex = record.TargetIndex;
            LowerLimit = record.LowerLimit;
            UpperLimit = record.UpperLimit;
            FinalRoundOnly = record.FinalRoundOnly;
            TargetItemID = record.TargetItemID;
        }

        public bool IsMet()
        {
            // FinalRoundOnly defers evaluation until the scenario's last round, so end-of-game
            // pollution thresholds don't fire on transient mid-game spikes. Implemented here so
            // SandboxManager.AnyLoseConditionMet doesn't need round bookkeeping.
            if (FinalRoundOnly)
            {
                SandboxManager mgr = SandboxManager.Instance;
                if (mgr == null) return false;
                if (mgr.CurrentRound < mgr.MaxRounds - 1) return false;
            }

            // TargetItemID, when set, overrides the def-lookup path so the condition can read
            // raw inventory keys (e.g. "sickness") that aren't registered as sellable Items.
            if (!string.IsNullOrEmpty(TargetItemID))
            {
                return IsItemMet(TargetItemID);
            }

            switch (Type)
            {
                case WinCondition.TargetType.Entity:
                    return IsEntityMet();
                case WinCondition.TargetType.Item:
                    return TargetItem != null && IsItemMet(TargetItem.ID);
                case WinCondition.TargetType.Currency:
                    return IsItemMet(PlayerInventory.CurrencyID);
                default:
                    return false;
            }
        }

        private bool IsEntityMet()
        {
            EntityManager em = SandboxManager.Instance?.EntityManager;
            if (em == null) return false;
            Entity type = em.GetEntityType(_targetIndex);
            if (type == null) return false;
            // Long-typed accessor so pollutant totals above int.MaxValue compare correctly.
            long total = em.GetTotalPopulationOfEntityTypeLong(_targetIndex);
            return IsWithinLimits(total);
        }

        private bool IsItemMet(string itemID)
        {
            PlayerInventory inventory = SandboxManager.Instance?.PlayerInventory;
            if (inventory == null) return false;
            return IsWithinLimits(inventory.GetAmountHeld(itemID)); //int promotes to long
        }

        // Mirrors WinCondition.IsWithinLimits so authors think in one set of rules: bracket
        // when both bounds are sensible, lower-bound-only otherwise. For lose conditions the
        // "danger" reading is INSIDE the bracket (e.g. phosphate total in [45, ∞] = collapse).
        // Comparisons run in double so float thresholds like 5.0e13 work against long inputs.
        private bool IsWithinLimits(long amount)
        {
            double lower = LowerLimit;
            double upper = UpperLimit;
            if (upper > 0.0 && upper >= lower)
            {
                return amount >= lower && amount <= upper;
            }
            return amount >= lower;
        }
    }
}
