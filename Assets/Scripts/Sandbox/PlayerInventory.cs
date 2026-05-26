using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Glitchers.EcoKnow.Sandbox
{
    public record Item(
        string ID,
        string Icon,
        int Value,
        bool CanSell,
        int BuyPrice = 0,
        int MaxQuantity = 0
    );

    public delegate void InventoryEvent(string id, int amount);

    public class PlayerInventory : MonoBehaviour
    {
        private List<Item> _itemDefs = new List<Item>();
        private List<Item> ItemDefs => _itemDefs == null ? null : _itemDefs.OrderBy(x => x.ID).ToList();
        private Dictionary<string, int> _inventory = new Dictionary<string, int>();
        public Dictionary<string, int> Inventory => _inventory;

        public const string CurrencyID = "currency"; //This currency is constant between all games and not dictated by a node
        public const string ActionID = "action"; //This currency is constant between all games and not dictated by a node

        private const string LogChannel = "[PlayerInventory]";

        public InventoryEvent onItemSold;
        public InventoryEvent onItemBought;

        public void Init()
        {
            _itemDefs.Clear();
            _inventory.Clear();
        }

        public void Cleanup()
        {
            onItemSold = null;
            onItemBought = null;
        }

        public void RegisterItemDefinitions(List<Item> items)
        {
            _itemDefs = items;
        }

        public Item GetItemDef(string id)
        {
            return ItemDefs.FirstOrDefault(x => x.ID.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        public Item GetItemDef(int index)
        {
            if (index < 0 || index >= ItemDefs.Count)
                return null;

            return ItemDefs[index];
        }

        public int GetItemIndex(string id)
        {
            return ItemDefs.FindIndex(x => x.ID.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        #region Add/Remove by Item ID
        public int AddItem(string id, int amount)
        {
            if (_inventory == null)
            {
                Debug.LogError($"{LogChannel} Cannot add item with id [{id}], inventory list is null!");
                return 0;
            }

            int amountHeld = 0;

            bool foundItem = _inventory.TryGetValue(id, out amountHeld);
            if (foundItem)
            {
                _inventory[id] += amount;
                amountHeld = _inventory[id];
            }
            else
            {
                _inventory.Add(id, amount);
                amountHeld = amount;
            }

            Debug.Log($"{LogChannel} Added {amount} [{id}]");

            return amountHeld;
        }

        public int RemoveItem(string id, int amount)
        {
            if (_inventory == null)
            {
                Debug.LogError($"{LogChannel} Cannot remove item with id [{id}], inventory list is null!");
                return 0;
            }

            int amountHeld = 0;

            bool foundItem = _inventory.TryGetValue(id, out amountHeld);
            if (foundItem)
            {
                _inventory[id] -= amount;
                if (_inventory[id] <= 0)
                {
                    _inventory.Remove(id);
                    amountHeld = 0;
                }
                else
                {
                    amountHeld = _inventory[id];
                }
            }
            else
            {
                //No item found
                Debug.LogError($"{LogChannel} Cannot remove item with id [{id}], none exist in the player's inventory!");
                return 0;
            }

            Debug.Log($"{LogChannel} Removed {amount} [{id}]");

            return amountHeld;
        }

        public bool SellItem(string id, int amount = 1)
        {
            int amountHeld = 0;
            bool foundItem = _inventory.TryGetValue(id, out amountHeld);
            if (foundItem && (amountHeld >= amount))
            {
                Item def = ItemDefs.FirstOrDefault(x => x.ID.Equals(id, System.StringComparison.OrdinalIgnoreCase));
                if ((def != null) && (def.CanSell))
                {
                    //Adjust inventory
                    RemoveItem(id, amount);
                    AddItem(CurrencyID, def.Value * amount);

                    onItemSold?.Invoke(id, amount);

                    return true;
                }
            }

            return false;
        }

        // Mirrors SellItem in shape. Buyables are item defs with BuyPrice > 0 (and an
        // optional MaxQuantity cap). Side-effect side of any buyable (e.g. the Water Treatment
        // Facility's region-install logic) lives upstream of this call — this method only
        // performs the currency-for-item swap and fires the onItemBought event.
        public bool BuyItem(string id, int amount = 1)
        {
            if (amount <= 0) return false;

            Item def = ItemDefs.FirstOrDefault(x => x.ID.Equals(id, System.StringComparison.OrdinalIgnoreCase));
            if (def == null || def.BuyPrice <= 0) return false;

            int held = GetAmountHeld(id);
            if (def.MaxQuantity > 0 && held + amount > def.MaxQuantity) return false;

            int totalCost = def.BuyPrice * amount;
            if (GetAmountHeld(CurrencyID) < totalCost) return false;

            //Adjust inventory
            RemoveItem(CurrencyID, totalCost);
            AddItem(id, amount);

            onItemBought?.Invoke(id, amount);

            return true;
        }

        public int GetAmountHeld(string id)
        {
            if (_inventory == null)
            {
                Debug.LogError($"{LogChannel} Cannot get item quantity with id [{id}], inventory list is null!");
                return 0;
            }

            int amountHeld = 0;
            bool foundItem = _inventory.TryGetValue(id, out amountHeld);
            if (foundItem)
            {
                return amountHeld;
            }

            return 0;
        }

        public List<Tuple<Item, int>> GetItemInventory()
        {
            List<Tuple<Item, int>> items = new List<Tuple<Item, int>>();

            //As we are searching ItemDefs we will only fetch items defined by the Item Nodes and not actions or currency
            foreach(KeyValuePair<string, int> item in _inventory)
            {
                Item def = ItemDefs.FirstOrDefault(x => x.ID.Equals(item.Key, System.StringComparison.OrdinalIgnoreCase));
                if (def != null)
                {
                    items.Add(new Tuple<Item, int>(def, item.Value));
                }
            }

            return items;
        }

        // Returns the rows the Inventory modal should display: every held item plus every
        // buyable item def that still has room to be purchased (BuyPrice > 0 and held below
        // MaxQuantity, treating MaxQuantity <= 0 as uncapped). Non-buyable item defs that the
        // player doesn't currently hold (e.g. oysters before the first fishing action) are
        // excluded so the modal doesn't show empty rows for them. Buyables are pinned to the
        // top of the returned list.
        public List<Tuple<Item, int>> GetItemDefsIncludingUnowned()
        {
            List<Tuple<Item, int>> items = new List<Tuple<Item, int>>();

            if (_itemDefs == null) return items;

            foreach (Item def in ItemDefs)
            {
                int held = 0;
                _inventory.TryGetValue(def.ID, out held);

                bool isBuyable = def.BuyPrice > 0;
                bool buyableHasRoom = isBuyable && (def.MaxQuantity <= 0 || held < def.MaxQuantity);

                //Owned items always appear. Buyables also appear when they still have room
                //to be purchased — so a maxed-out buyable falls back to the owned-only path
                //and renders as a normal non-sellable row instead of a "buy" row.
                if (held > 0 || buyableHasRoom)
                {
                    items.Add(new Tuple<Item, int>(def, held));
                }
            }

            //Pin buyables (BuyPrice > 0) to the top while preserving the rest of the order.
            items = items
                .OrderByDescending(x => x.Item1.BuyPrice > 0 ? 1 : 0)
                .ToList();

            return items;
        }
        #endregion

        #region Add/Remove By Quantity
        public bool HasQuantities(Quantity[] itemQuantities, int multiplier = 1)
        {
            //Do we have a valid inventory? 
            if ((_inventory == null) || (_inventory.Count <= 0))
            {
                return false;
            }

            //Are the quantities valid?
            if ((itemQuantities == null) || (itemQuantities.Count() <= 0))
            {
                return false;
            }

            //Check quantities against inventory
            foreach (Quantity quantity in itemQuantities)
            {
                //Debug.Log($"{LogChannel} [{quantity.ID}] Required: {quantity.Value * multiplier} / Owned: {GetAmountHeld(quantity.ID)}");

                if (GetAmountHeld(quantity.ID) < (quantity.Value * multiplier))
                {
                    return false;
                }
            }

            return true;
        }

        public void AddQuantities(Quantity[] itemQuantities, int multiplier = 1)
        {
            //Do we have a valid inventory? 
            if ((_inventory == null) || (_inventory.Count <= 0))
            {
                return;
            }

            //Are the quantities valid?
            if ((itemQuantities == null) || (itemQuantities.Count() <= 0))
            {
                return;
            }

            //Add items
            foreach (Quantity quantity in itemQuantities)
            {
                AddItem(quantity.ID, quantity.Value * multiplier);
            }
        }

        public void RemoveQuantities(Quantity[] itemQuantities, int multiplier = 1)
        {
            //Do we have a valid inventory? 
            if ((_inventory == null) || (_inventory.Count <= 0))
            {
                return;
            }

            //Are the quantities valid?
            if ((itemQuantities == null) || (itemQuantities.Count() <= 0))
            {
                return;
            }

            //Add items
            foreach (Quantity quantity in itemQuantities)
            {
                RemoveItem(quantity.ID, quantity.Value * multiplier);
            }
        }
        #endregion
    }
}
