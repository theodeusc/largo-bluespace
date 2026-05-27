using System;
using System.Collections.Generic;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    // Maps an entity ID (e.g. "oyster", "litter") to the single direct-action the ToolPanel
    // should offer when the player clicks that entity's row in EntityPanel. Entities not in
    // the registry get no action button — ToolPanel falls back to "name + count only".
    //
    // To add a new entity action: append one entry to the static initializer below. The
    // descriptor encapsulates label, cost text, the availability predicate, and the
    // invocation. No callsite changes needed.
    public static class EntityActionRegistry
    {
        private static readonly Dictionary<string, EntityActionDescriptor> s_actions =
            new Dictionary<string, EntityActionDescriptor>
            {
                {
                    WaterGameActions.OysterEntityId,
                    new EntityActionDescriptor(
                        label: "Forage",
                        costText: "1 AP",
                        canPerform: WaterGameActions.CanFish,
                        perform: WaterGameActions.DoFish)
                },
                {
                    WaterGameActions.LitterEntityId,
                    new EntityActionDescriptor(
                        label: "Pick",
                        costText: "1 AP",
                        canPerform: WaterGameActions.CanPickLitter,
                        perform: WaterGameActions.DoPickLitter)
                },
            };

        public static bool TryGetAction(string entityId, out EntityActionDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(entityId))
            {
                descriptor = default;
                return false;
            }
            return s_actions.TryGetValue(entityId, out descriptor);
        }
    }

    public readonly struct EntityActionDescriptor
    {
        public readonly string Label;
        public readonly string CostText;
        public readonly Func<bool> CanPerform;
        public readonly Action Perform;

        public EntityActionDescriptor(string label, string costText, Func<bool> canPerform, Action perform)
        {
            Label = label;
            CostText = costText;
            CanPerform = canPerform;
            Perform = perform;
        }
    }
}
