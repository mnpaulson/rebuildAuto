using System;
using System.Collections.Generic;
using UnityEngine;

namespace RebuildBotPlugin.Services
{
    public class ShopItemEntry
    {
        public string CanonicalName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string VendorMap { get; set; } = "";
        public string NpcName { get; set; } = "Weapon Dealer";
        public Vector2Int NpcPosition { get; set; }
        public int BasePrice { get; set; }
        public int MinLevel { get; set; } = 1;
        public int WeaponRank { get; set; } = 0; // 0 = Armor, 1-4 = Weapon Rank
        public string EquipSlot { get; set; } = "Weapon";
    }

    public static class ShopRegistry
    {
        private static readonly Dictionary<string, ShopItemEntry> items = new(StringComparer.OrdinalIgnoreCase);

        static ShopRegistry()
        {
            void Add(string name, string map, string npc, int x, int y, int price, int minLvl, int rank, string slot, string disp = null)
            {
                var entry = new ShopItemEntry
                {
                    CanonicalName = name,
                    DisplayName = disp ?? name.Replace('_', ' '),
                    VendorMap = map,
                    NpcName = npc,
                    NpcPosition = new Vector2Int(x, y),
                    BasePrice = price,
                    MinLevel = minLvl,
                    WeaponRank = rank,
                    EquipSlot = slot
                };
                items[name] = entry;
                string noUnder = name.Replace('_', ' ');
                if (!items.ContainsKey(noUnder)) items[noUnder] = entry;
            }

            // --- ALBERTA WEAPON & ARMOR DEALER (alberta_in) ---
            Add("Battle_Axe", "alberta_in", "Weapon Dealer", 188, 21, 5400, 1, 1, "Weapon");
            Add("Hammer", "alberta_in", "Weapon Dealer", 188, 21, 15500, 16, 2, "Weapon");
            Add("Buster", "alberta_in", "Weapon Dealer", 188, 21, 34000, 24, 2, "Weapon");
            Add("Two-Handed_Axe", "alberta_in", "Weapon Dealer", 188, 21, 55000, 30, 3, "Weapon", "Two-Handed Axe");
            Add("Axe", "alberta_in", "Weapon Dealer", 188, 21, 500, 1, 1, "Weapon");

            Add("Guard", "alberta_in", "Armor Dealer", 180, 15, 500, 1, 0, "Shield");
            Add("Buckler", "alberta_in", "Armor Dealer", 180, 15, 14000, 1, 0, "Shield");
            Add("Sandals", "alberta_in", "Armor Dealer", 180, 15, 400, 1, 0, "Footgear");
            Add("Shoes", "alberta_in", "Armor Dealer", 180, 15, 3500, 1, 0, "Footgear");
            Add("Boots", "alberta_in", "Armor Dealer", 180, 15, 18000, 1, 0, "Footgear");
            Add("Hood", "alberta_in", "Armor Dealer", 180, 15, 1000, 1, 0, "Garment");
            Add("Muffler", "alberta_in", "Armor Dealer", 180, 15, 5000, 1, 0, "Garment");
            Add("Manteau", "alberta_in", "Armor Dealer", 180, 15, 32000, 1, 0, "Garment");

            // --- PRONTERA WEAPON & ARMOR DEALER (prt_in) ---
            Add("Sword", "prt_in", "Weapon Dealer", 172, 130, 100, 1, 1, "Weapon");
            Add("Falchion", "prt_in", "Weapon Dealer", 172, 130, 1500, 1, 1, "Weapon");
            Add("Blade", "prt_in", "Weapon Dealer", 172, 130, 2900, 1, 1, "Weapon");
            Add("Rapier", "prt_in", "Weapon Dealer", 172, 130, 10000, 14, 2, "Weapon");
            Add("Scimiter", "prt_in", "Weapon Dealer", 172, 130, 17000, 14, 2, "Weapon");
            Add("Ring_Pommel_Saber", "prt_in", "Weapon Dealer", 172, 130, 24000, 18, 2, "Weapon", "Ring Pommel Saber");
            Add("Tsurugi", "prt_in", "Weapon Dealer", 172, 130, 51000, 27, 3, "Weapon");
            Add("Haedonggum", "prt_in", "Weapon Dealer", 172, 130, 50000, 27, 3, "Weapon");
            Add("Saber", "prt_in", "Weapon Dealer", 172, 130, 49000, 27, 3, "Weapon");
            Add("Flamberge", "prt_in", "Weapon Dealer", 172, 130, 60000, 40, 3, "Weapon");

            Add("Javelin", "prt_in", "Weapon Dealer", 171, 140, 150, 1, 1, "Weapon");
            Add("Spear", "prt_in", "Weapon Dealer", 171, 140, 1700, 1, 1, "Weapon");
            Add("Pike", "prt_in", "Weapon Dealer", 171, 140, 3450, 1, 1, "Weapon");
            Add("Guisarme", "prt_in", "Weapon Dealer", 171, 140, 13000, 14, 2, "Weapon");
            Add("Glaive", "prt_in", "Weapon Dealer", 171, 140, 20000, 14, 2, "Weapon");
            Add("Partizan", "prt_in", "Weapon Dealer", 171, 140, 27000, 18, 2, "Weapon");
            Add("Trident", "prt_in", "Weapon Dealer", 171, 140, 51000, 27, 3, "Weapon");
            Add("Halberd", "prt_in", "Weapon Dealer", 171, 140, 54000, 27, 3, "Weapon");
            Add("Lance", "prt_in", "Weapon Dealer", 171, 140, 60000, 40, 3, "Weapon");

            Add("Cotton_Shirt", "prt_in", "Armor Dealer", 172, 132, 10, 1, 0, "Armor", "Cotton Shirt");
            Add("Jacket", "prt_in", "Armor Dealer", 172, 132, 200, 1, 0, "Armor");
            Add("Adventurer's_Suit", "prt_in", "Armor Dealer", 172, 132, 1000, 1, 0, "Armor", "Adventurer's Suit");
            Add("Wooden_Mail", "prt_in", "Armor Dealer", 172, 132, 5500, 14, 0, "Armor", "Wooden Mail");
            Add("Mantle", "prt_in", "Armor Dealer", 172, 132, 10000, 14, 0, "Armor");
            Add("Coat", "prt_in", "Armor Dealer", 172, 132, 11000, 14, 0, "Armor");
            Add("Padded_Armor", "prt_in", "Armor Dealer", 172, 132, 28000, 18, 0, "Armor", "Padded Armor");
            Add("Chain_Mail", "prt_in", "Armor Dealer", 172, 132, 65000, 24, 0, "Armor", "Chain Mail");

            // --- PRONTERA CHURCH (prt_church) ---
            Add("Club", "prt_church", "Nun", 108, 124, 100, 1, 1, "Weapon");
            Add("Mace", "prt_church", "Nun", 108, 124, 2500, 1, 1, "Weapon");
            Add("Smasher", "prt_church", "Nun", 108, 124, 9000, 14, 2, "Weapon");
            Add("Flail", "prt_church", "Nun", 108, 124, 16000, 14, 2, "Weapon");
            Add("Morning_Star", "prt_church", "Nun", 108, 124, 43000, 27, 3, "Weapon", "Morning Star");
            Add("Chain", "prt_church", "Nun", 108, 124, 23000, 18, 2, "Weapon");
            Add("Saint's_Robe", "prt_church", "Nun", 108, 124, 54000, 24, 0, "Armor", "Saint's Robe");

            // --- PAYON BOWS & ARMOR (payon_in01) ---
            Add("Bow", "payon_in01", "Weapon Dealer", 15, 119, 1000, 1, 1, "Weapon");
            Add("Composite_Bow", "payon_in01", "Weapon Dealer", 15, 119, 2500, 1, 1, "Weapon", "Composite Bow");
            Add("Great_Bow", "payon_in01", "Weapon Dealer", 15, 119, 10000, 18, 2, "Weapon", "Great Bow");
            Add("Cross_Bow", "payon_in01", "Weapon Dealer", 15, 119, 17000, 18, 2, "Weapon", "Cross Bow");
            Add("Arbalest_Bow", "payon_in01", "Weapon Dealer", 15, 119, 48000, 33, 3, "Weapon", "Arbalest Bow");
            Add("Gakkung_Bow", "payon_in01", "Weapon Dealer", 15, 119, 42000, 33, 3, "Weapon", "Gakkung Bow");
            Add("Hunter_Bow", "payon_in01", "Weapon Dealer", 15, 119, 64000, 55, 3, "Weapon", "Hunter Bow");
            Add("Silk_Robe", "payon_in01", "Armor Dealer", 7, 119, 8000, 1, 0, "Armor", "Silk Robe");
            Add("Silver_Robe", "payon_in01", "Armor Dealer", 7, 119, 7000, 18, 0, "Armor", "Silver Robe");
            Add("Tights", "payon_in01", "Armor Dealer", 7, 119, 71000, 30, 0, "Armor");

            // --- MORROC DAGGERS & KATARS (morocc_in) ---
            Add("Knife", "morocc_in", "Weapon Dealer", 141, 67, 50, 1, 1, "Weapon");
            Add("Cutter", "morocc_in", "Weapon Dealer", 141, 67, 1250, 1, 1, "Weapon");
            Add("Main_Gauche", "morocc_in", "Weapon Dealer", 141, 67, 2400, 1, 1, "Weapon", "Main Gauche");
            Add("Dirk", "morocc_in", "Weapon Dealer", 141, 67, 8500, 12, 2, "Weapon");
            Add("Dagger", "morocc_in", "Weapon Dealer", 141, 67, 14000, 12, 2, "Weapon");
            Add("Stiletto", "morocc_in", "Weapon Dealer", 141, 67, 19500, 12, 2, "Weapon");
            Add("Gladius", "morocc_in", "Weapon Dealer", 141, 67, 43000, 24, 3, "Weapon");
            Add("Damascus", "morocc_in", "Weapon Dealer", 141, 67, 49000, 24, 3, "Weapon");
            Add("Jur", "morocc_in", "Weapon Dealer", 141, 67, 28000, 18, 2, "Weapon");
            Add("Katar", "morocc_in", "Weapon Dealer", 141, 67, 41000, 24, 3, "Weapon");
            Add("Jamadhar", "morocc_in", "Weapon Dealer", 141, 67, 37000, 24, 3, "Weapon");
            Add("Thief_Clothes", "morocc_in", "Armor Dealer", 141, 60, 74000, 30, 0, "Armor", "Thief Clothes");

            // --- GEFFEN RODS & STAVES (geffen_in) ---
            Add("Rod", "geffen_in", "Magical Item Seller", 77, 173, 50, 1, 1, "Weapon");
            Add("Wand", "geffen_in", "Magical Item Seller", 77, 173, 2500, 1, 1, "Weapon");
            Add("Staff", "geffen_in", "Magical Item Seller", 77, 173, 9500, 12, 2, "Weapon");
            Add("Arc_Wand", "geffen_in", "Magical Item Seller", 77, 173, 45000, 24, 3, "Weapon", "Arc Wand");

            // --- IZLUDE 2H SWORDS & HEAVY ARMOR (izlude_in) ---
            Add("Katana", "izlude_in", "Weapon Dealer", 60, 127, 2000, 1, 1, "Weapon");
            Add("Slayer", "izlude_in", "Weapon Dealer", 60, 127, 34000, 24, 2, "Weapon");
            Add("Bastard_Sword", "izlude_in", "Weapon Dealer", 60, 127, 22500, 20, 2, "Weapon", "Bastard Sword");
            Add("Two-Handed_Sword", "izlude_in", "Weapon Dealer", 60, 127, 60000, 33, 3, "Weapon", "Two-Handed Sword");
            Add("Broad_Sword", "izlude_in", "Weapon Dealer", 60, 127, 65000, 48, 3, "Weapon", "Broad Sword");
            Add("Shield", "izlude_in", "Armor Dealer", 70, 127, 56000, 1, 0, "Shield");
            Add("Full_Plate", "izlude_in", "Armor Dealer", 70, 127, 80000, 40, 0, "Armor", "Full Plate");
        }

        public static bool TryGetEntry(string itemName, out ShopItemEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(itemName)) return false;
            return items.TryGetValue(itemName.Trim(), out entry);
        }

        public static IEnumerable<ShopItemEntry> GetAllEntries()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in items)
            {
                if (seen.Add(kvp.Value.CanonicalName))
                {
                    yield return kvp.Value;
                }
            }
        }
    }
}
