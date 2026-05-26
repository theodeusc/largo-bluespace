using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class InventoryModal : MonoBehaviour
    {
        [SerializeField] private TMPro.TMP_Text _currency;
        [SerializeField] private InventoryRow _inventoryRowPrefab;
        [SerializeField] private RectTransform _inventoryRowContainer;

        [Header("Buttons")]
        [SerializeField] private Button _sellButton;
        [SerializeField] private TMP_Text _unitText;
        [SerializeField] private TMP_Text _profitText;
        [SerializeField] private Button _cancelButton;

        public Action onSellSuccess;

        private InventoryRow[] _inventoryRowList => _inventoryRowContainer == null ? null : _inventoryRowContainer.GetComponentsInChildren<InventoryRow>();

        private void Start()
        {
            HideModal();
        }

        public void ShowModal()
        {
            this.gameObject.SetActive(true);
            RefreshInventory();
            OnUnitsAdjusted();
        }

        public void HideModal()
        {
            OnCancelPressed();
            this.gameObject.SetActive(false);
        }

        public void OnSellPressed() => OnCommitPressed();

        // Polymorphic commit: sell-mode rows go through PlayerInventory.SellItem (existing path),
        // buy-mode rows route to the matching SandboxManager entry point (currently only the
        // Water Treatment Facility's region-install action). The bottom modal button calls this
        // single handler regardless of which mode rows are selected; OnUnitsAdjusted handles
        // the button's label/state so the player sees "Buy" or "Sell" appropriately.
        public void OnCommitPressed()
        {
            if (!SandboxManager.CanPerformAction())
            {
                return;
            }

            if (_inventoryRowList == null) return;

            InventoryRow[] selectedRows = _inventoryRowList.Where(x => x.IsSelectedForSell).ToArray();
            if (selectedRows == null || selectedRows.Length == 0) return;

            bool anyCommit = false;

            foreach (InventoryRow row in selectedRows)
            {
                if (row.IsBuyMode)
                {
                    //Currently only the Water Treatment Facility is a buyable; route to its
                    //single source-of-truth purchase method on SandboxManager so the region
                    //install + tint clear run alongside the inventory mutation.
                    if (row.ItemID == SandboxManager.WaterTreatmentItemID)
                    {
                        if (SandboxManager.Instance.TryBuyWaterTreatment()) anyCommit = true;
                    }
                    else
                    {
                        //Future generic buyables can route through the inventory-only path.
                        if (SandboxManager.Instance.PlayerInventory?.BuyItem(row.ItemID, row.SelectedUnits) == true)
                            anyCommit = true;
                    }
                }
                else
                {
                    if (SandboxManager.Instance.PlayerInventory?.SellItem(row.ItemID, row.SelectedUnits) == true)
                        anyCommit = true;
                }

                row.ClearSelection();
            }

            RefreshInventory();
            OnUnitsAdjusted();

            if (anyCommit)
            {
                onSellSuccess?.Invoke();
            }
        }

        public void OnCancelPressed()
        {
            if (_inventoryRowList != null)
            {
                foreach (InventoryRow row in _inventoryRowList)
                {
                    row.ClearSelection();
                }

                RefreshInventory();
                OnUnitsAdjusted();
            }
        }

        public void OnUnitsAdjusted()
        {
            bool anySelected = false;
            bool anyBuySelected = false;
            int totalUnits = 0;
            int totalAmount = 0;
            foreach (InventoryRow row in _inventoryRowList)
            {
                if (row.IsSelectedForSell)
                {
                    anySelected = true;
                    totalUnits += row.SelectedUnits;
                    if (row.IsBuyMode)
                    {
                        anyBuySelected = true;
                        totalAmount += (row.SelectedUnits * row.BuyPrice);
                    }
                    else
                    {
                        totalAmount += (row.SelectedUnits * row.ItemValue);
                    }
                }
            }

            //Commit button interactable when something is selected AND, for buy rows, the
            //player still satisfies the gating predicate (round/currency/region-mode/etc.).
            bool buyAllowed = !anyBuySelected || (SandboxManager.Exists && SandboxManager.Instance.CanBuyWaterTreatment());

            if (_sellButton != null)
            {
                _sellButton.interactable = anySelected && SandboxManager.CanPerformAction() && buyAllowed;
            }

            if (_unitText != null)
            {
                if (anyBuySelected)
                {
                    string unitWord = totalUnits == 1 ? "Unit" : "Units";
                    _unitText.text = $"Buy {totalUnits.ToString("n0")} {unitWord} for ";
                }
                else
                {
                    _unitText.text = $"Sell {totalUnits.ToString("n0")} Units for ";
                }
            }

            if (_profitText != null)
            {
                _profitText.text = totalAmount.ToString("n0");
            }
        }

        private void RefreshInventory()
        {

            foreach(InventoryRow child in _inventoryRowContainer.GetComponentsInChildren<InventoryRow>())
            {
                Destroy(child.gameObject);
            }

            if (_inventoryRowPrefab == null)
            {
                return;
            }

            PlayerInventory inventory = SandboxManager.Instance.PlayerInventory;
            if (inventory != null)
            {
                //Currency
                if (_currency != null)
                {
                    _currency.text = string.Format($"�{inventory.GetAmountHeld(PlayerInventory.CurrencyID)}");
                }

                //Items — include defs the player doesn't yet hold so buyables (e.g. the Water
                //Treatment Facility) appear as greyed rows pinned to the top of the list.
                foreach (Tuple<Item, int> item in inventory.GetItemDefsIncludingUnowned())
                {
                    InventoryRow row = Instantiate(_inventoryRowPrefab, _inventoryRowContainer);
                    if (row != null)
                    {
                        row.Init(item.Item1, item.Item2, delegate { OnUnitsAdjusted(); } );
                    }
                }
            }

            if (this.GetComponent<RectTransform>() != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(this.GetComponent<RectTransform>());
            }
        }
    }
}
