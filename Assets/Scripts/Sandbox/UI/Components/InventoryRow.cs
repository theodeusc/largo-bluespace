using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class InventoryRow : MonoBehaviour
    {
        [Header("Text")]
        [SerializeField] private TMP_Text _itemName;
        [SerializeField] private TMP_Text _itemQuantity;
        [SerializeField] private TMP_Text _itemCost;
        [SerializeField] private TMP_Text _buttonText;

        [SerializeField] private TMP_InputField _inputField;

        [Header("Graphics")]
        [SerializeField] private Image _itemIcon;
        [SerializeField] private Image _rowBackground;
        [SerializeField] private GameObject _quantitySelector;
        [SerializeField] private GameObject _cannotSellBumper;

        [Header("Buttons")]
        [SerializeField] private Button _decreaseButton;
        [SerializeField] private Button _increaseButton;

        [Header("Colours")]
        [SerializeField] private Color _defaultBackgroundColour;
        [SerializeField] private Color _selectedBackgroundColour;
        [SerializeField] private Color _greyedOutColour = new Color(0.6f, 0.6f, 0.6f, 1f);

        private Item itemDef;
        public string ItemID => itemDef == null ? string.Empty : itemDef.ID;
        public int ItemValue => itemDef == null ? 0 : itemDef.Value;

        // True when the row represents an item the player can purchase (BuyPrice > 0 and the
        // current held count is below MaxQuantity). The modal reads this to decide whether
        // the bottom commit button should label itself "Buy" and route the click through the
        // buy path rather than SellItem.
        private bool _isBuyMode;
        public bool IsBuyMode => _isBuyMode;
        public int BuyPrice => itemDef == null ? 0 : itemDef.BuyPrice;

        // Cached at Init time so the grey-out tint reflects affordability ("can the player
        // afford one unit at the row's BuyPrice?") rather than purchase state. Refreshed
        // each time the modal calls Init via RefreshInventory.
        private bool _canAffordBuy;

        private int maxUnits = 0;

        private UnityAction OnUnitsAdjusted;

        public bool IsSelectedForSell => SelectedUnits > 0;
        public int SelectedUnits
        {
            get
            {
                int inputValue = 0;
                if (_inputField != null)
                {
                    int.TryParse(_inputField.text, System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.CurrentCulture, out inputValue);
                }

                //No negatives!
                if (inputValue < 0)
                {
                    inputValue = 0;
                }

                return inputValue;
            }
        }

        private const string LogChannel = "[InventoryRow]";

        // Step size for the +/- buttons in the sell modal. Fishing yields ~24 oysters per
        // action (HarvestLimit), so stepping by a dozen lets two clicks (or one click + clamp)
        // ladder up to a full catch without typing into the input field. Direct text input
        // still bypasses this step for fine-grained sales.
        private const int UnitStepSize = 12;

        public void Init(Item item, int amount, UnityAction onUnitsAdjusted)
        {
            itemDef = item;

            //Buyables: row appears even before purchase, with a quantity selector capped to
            //MaxQuantity. After the cap is reached the row drops into the standard owned-item
            //view (greyed-off + cannot-sell bumper, no buy controls).
            _isBuyMode = item.BuyPrice > 0
                && (item.MaxQuantity <= 0 || amount < item.MaxQuantity);

            if (_isBuyMode)
            {
                maxUnits = item.MaxQuantity > 0 ? item.MaxQuantity : 1;
            }
            else
            {
                maxUnits = amount;
            }

            SetIcon(item.Icon);

            //Quantity selector visibility:
            //  • Sellable owned item → show selector (existing behaviour)
            //  • Buyable not-yet-purchased → show selector (clicks pick "Buy 1 Unit")
            //  • Otherwise (owned non-sellable / capped buyable) → show the cannot-sell bumper
            bool showSelector = itemDef.CanSell || _isBuyMode;
            _quantitySelector?.SetActive(showSelector);
            _cannotSellBumper?.SetActive(!showSelector);

            //Set text
            if (_itemName != null)
            {
                _itemName.text = HumaniseID(item.ID);
            }

            if (_itemQuantity != null)
            {
                _itemQuantity.text = amount.ToString("n0");
            }

            if (_itemCost != null)
            {
                int displayCost = _isBuyMode ? item.BuyPrice : item.Value;
                _itemCost.text = displayCost.ToString("n0");
            }

            if (_inputField != null)
            {
                _inputField.text = "0";
                //In buy mode we don't let the user type a quantity — selection is the +/- toggle.
                _inputField.interactable = !_isBuyMode;
            }

            //Cache affordability so UpdateSelectedState can tint the row grey only when the
            //player can't yet afford the purchase. Owned items (non-buy mode) are always
            //"affordable" for tinting purposes — they use the standard default background.
            if (_isBuyMode && SandboxManager.Exists && SandboxManager.Instance.PlayerInventory != null)
            {
                int currency = SandboxManager.Instance.PlayerInventory.GetAmountHeld(PlayerInventory.CurrencyID);
                _canAffordBuy = currency >= item.BuyPrice;
            }
            else
            {
                _canAffordBuy = true;
            }

            OnUnitsAdjusted = onUnitsAdjusted;

            UpdateSelectedState();
        }

        // Mirrors ItemWidget.SetValid: tints the row background to indicate "this row is not
        // currently actionable" (e.g. a buyable item the player has not yet purchased).
        public void SetValid(bool value)
        {
            if (_rowBackground != null)
            {
                _rowBackground.color = value ? _defaultBackgroundColour : _greyedOutColour;
            }
        }

        // Convert an item ID like "water_treatment_facility" into a human-readable display
        // name like "Water Treatment Facility". The existing sellable items (e.g. "oyster")
        // already have ID == single-word display label, so the underscore split keeps them
        // unchanged while pixel-art-id buyables read correctly.
        private static string HumaniseID(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            string[] parts = id.Replace('-', '_').Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
            return string.Join(" ", parts);
        }

        private void SetIcon(string spritePath)
        {
            if ((_itemIcon != null) && !string.IsNullOrEmpty(spritePath))
            {
                Sprite resource = Resources.Load<Sprite>(spritePath);
                if (resource != null)
                {
                    _itemIcon.sprite = resource;
                    _itemIcon.color = Color.white;
                }
                else
                {
                    Debug.LogError($"{LogChannel} Failed to find icon for item at path {spritePath}!");
                }
            }
        }

        private void UpdateSelectedState()
        {
            if (_rowBackground == null) return;

            if (IsSelectedForSell)
            {
                //Selected — same highlight in either mode so the player gets consistent feedback.
                _rowBackground.color = _selectedBackgroundColour;
            }
            else if (_isBuyMode && !_canAffordBuy)
            {
                //Unselected buyable AND the player can't afford it — telegraph the blocker
                //with the grey tint. Once the player has enough currency the row uses the
                //normal default colour so "available" reads clearly.
                _rowBackground.color = _greyedOutColour;
            }
            else
            {
                _rowBackground.color = _defaultBackgroundColour;
            }
        }

        #region Adjust Units
        public void OnIncreasePressed()
        {
            if (_inputField != null)
            {
                // Step by a dozen, then clamp to the max in ValidateInput so the last
                // partial step lands exactly at maxUnits instead of overshooting.
                int target = SelectedUnits + UnitStepSize;
                if (target > maxUnits) target = maxUnits;
                _inputField.text = target.ToString();
            }

            OnInputModified();
        }

        public void OnDecreasePressed()
        {
            if (_inputField != null)
            {
                int target = SelectedUnits - UnitStepSize;
                if (target < 0) target = 0;
                _inputField.text = target.ToString();
            }

            OnInputModified();
        }

        public void OnInputModified()
        {
            ValidateInput();
            UpdateSelectedState();
            OnUnitsAdjusted?.Invoke();
        }

        public void ValidateInput()
        {
            //Ensure input field looks correct
            if (SelectedUnits <= 0)
            {
                //No negatives
                if (_inputField != null)
                {
                    _inputField.text = "0";
                }

                //Sort out buttons
                if (_decreaseButton != null)
                {
                    _decreaseButton.interactable = false;
                }

                if (_increaseButton != null)
                {
                    _increaseButton.interactable = true;
                }
            }
            else
            {
                //Can't select more than we have
                int clampedInput = Mathf.Clamp(SelectedUnits, 0, maxUnits);
                if (_inputField != null)
                {
                    _inputField.text = clampedInput.ToString("n0");
                }

                //Sort out buttons
                if (_decreaseButton != null)
                {
                    _decreaseButton.interactable = true;
                }

                if (_increaseButton != null)
                {
                    _increaseButton.interactable = SelectedUnits < maxUnits;
                }
            }
        }

        public void ClearSelection()
        {
            if (_inputField != null)
            {
                _inputField.text = "0";
            }

            OnInputModified();
        }
        #endregion
    }
}
