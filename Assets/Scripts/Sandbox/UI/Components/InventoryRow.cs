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

        private Item itemDef;
        public string ItemID => itemDef == null ? string.Empty : itemDef.ID;
        public int ItemValue => itemDef == null ? 0 : itemDef.Value;

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
            maxUnits = amount;

            SetIcon(item.Icon);

            _quantitySelector?.SetActive(itemDef.CanSell);
            _cannotSellBumper?.SetActive(!itemDef.CanSell);

            //Set text
            if (_itemName != null)
            {
                _itemName.text = item.ID;
            }

            if (_itemQuantity != null)
            {
                _itemQuantity.text = amount.ToString("n0");
            }

            if (_itemCost != null)
            {
                _itemCost.text = item.Value.ToString("n0");
            }

            if (_inputField != null)
            {
                _inputField.text = "0";
            }

            OnUnitsAdjusted = onUnitsAdjusted;

            UpdateSelectedState();
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
            if (_rowBackground != null)
            {
                _rowBackground.color = IsSelectedForSell ? _selectedBackgroundColour : _defaultBackgroundColour;
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
