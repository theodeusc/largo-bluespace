using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class InventoryPanel : MonoBehaviour
    {
        [Header("Item Widgets")]
        [SerializeField] private int _widgetCount = 4;
        [SerializeField] private Transform _widgetContainer;
        [SerializeField] ItemWidget[] _enabledWidgets;
        [SerializeField] ItemWidget[] _disabledWidgets;

        public void RefreshInventory()
        {
            DisableWidgets();

            if ((_enabledWidgets == null) || (_disabledWidgets == null)
                || (_enabledWidgets.Count() <= 0) || (_disabledWidgets.Count() <= 0))
            {
                return;
            }

            PlayerInventory inventory = SandboxManager.Instance.PlayerInventory;
            if (inventory == null) return;

            //Pin buyables (e.g. the Water Treatment Facility) to the first slot(s) of the
            //panel and grey them out when the player hasn't yet purchased them. After
            //purchase the widget renders normally — same icon, quantity 1, no grey tint.
            //We sort owned-only items into the remaining slots, biggest stack first.
            List<Tuple<Item, int>> pinnedBuyables = inventory.GetItemDefsIncludingUnowned()
                .Where(x => x.Item1.BuyPrice > 0)
                .ToList();

            HashSet<string> pinnedIds = new HashSet<string>(
                pinnedBuyables.Select(x => x.Item1.ID),
                StringComparer.OrdinalIgnoreCase);

            List<Tuple<Item, int>> ownedItems = inventory.GetItemInventory()
                .Where(x => !x.Item1.ID.Equals(PlayerInventory.CurrencyID)
                    && !x.Item1.ID.Equals(PlayerInventory.ActionID)
                    && !pinnedIds.Contains(x.Item1.ID))
                .OrderByDescending(x => x.Item2)
                .ToList();

            List<Tuple<Item, int>> displayItems = new List<Tuple<Item, int>>(pinnedBuyables);
            displayItems.AddRange(ownedItems);

            int currency = inventory.GetAmountHeld(PlayerInventory.CurrencyID);

            for (int i = 0; i < 4; i++)
            {
                if (i < displayItems.Count)
                {
                    Tuple<Item, int> item = displayItems[i];
                    ItemWidget widget = _enabledWidgets[i];

                    //Show our item in a widget
                    widget.ShowTitle(false);
                    widget.SetIcon(item.Item1.Icon);
                    widget.SetQuantity(item.Item2);

                    //Buyables render greyed only when the player can't afford one unit.
                    //Once they have enough currency the widget snaps back to the normal
                    //tint so "available to buy" reads clearly even before the purchase
                    //actually happens. Already-owned buyables (qty > 0) read as normal.
                    bool isBuyable = item.Item1.BuyPrice > 0;
                    bool greyOut = isBuyable && item.Item2 <= 0 && currency < item.Item1.BuyPrice;
                    widget.SetValid(!greyOut);

                    widget.gameObject.SetActive(true);
                }
                else
                {
                    //Activate a disabled widget
                    ItemWidget widget = _disabledWidgets[i];
                    widget.gameObject.SetActive(true);
                }
            }
        }

        private void DisableWidgets()
        {
            foreach(ItemWidget widget in _enabledWidgets)
            {
                widget.gameObject.SetActive(false);
            }

            foreach (ItemWidget widget in _disabledWidgets)
            {
                widget.gameObject.SetActive(false);
            }
        }
    }
}

