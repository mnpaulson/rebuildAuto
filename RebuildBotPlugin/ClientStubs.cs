using System.Collections.Generic;
using RebuildSharedData.Enum;
using UnityEngine;

namespace Assets.Scripts.Network
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance;

        public Dictionary<int, ServerControllable> EntityList = new Dictionary<int, ServerControllable>();
        public Dictionary<int, GroundItem> GroundItemList = new Dictionary<int, GroundItem>();
        public int PlayerId;
        public string CurrentMap = "";

        public void SendAttack(int targetId) { }
        public void SendPickUpItem(int groundItemId) { }
        public void SendMoveRequest(string map, int x, int y, bool isPath = false) { }
        public void SendUseItem(int itemId) { }
    }

    public class ServerControllable : MonoBehaviour
    {
        public CharacterType CharacterType;
        public CharacterState CharacterState;
        public int Id;
        public Vector2Int CellPosition;
        public bool IsAttackable;
        public string Name;
        public int Hp;
        public int MaxHp;
        public int Sp;
        public int MaxSp;
    }

    public class GroundItem : MonoBehaviour
    {
        public int EntityId;
        public int ItemId;
        public int Count;
        public string ItemName;
    }
}

namespace Assets.Scripts.MapEditor
{
    public class RagnarokWalkData : ScriptableObject
    {
        public int Width;
        public int Height;
        public bool CellWalkable(int x, int y) => true;
    }

    public class RoWalkDataProvider : MonoBehaviour
    {
        public static RoWalkDataProvider Instance;
        public RagnarokWalkData WalkData;
        public bool IsCellWalkable(Vector2Int cell) => true;
    }
}


namespace Assets.Scripts.PlayerControl
{
    public struct InventoryItem
    {
        public int Id;
        public int BagSlotId;
        public RebuildSharedData.ClientTypes.ItemData ItemData;
        public int Count;
    }

    public class ClientInventory : MonoBehaviour
    {
        public static ClientInventory Instance;
        public List<InventoryItem> Items = new List<InventoryItem>();
    }
}

namespace Assets.Scripts.Sprites
{
    public class ClientDataLoader : MonoBehaviour
    {
        public static ClientDataLoader Instance;
        public Dictionary<int, RebuildSharedData.ClientTypes.MonsterClassData> MonsterClassLookup = new Dictionary<int, RebuildSharedData.ClientTypes.MonsterClassData>();
        public Dictionary<int, RebuildSharedData.ClientTypes.ItemData> ItemIdLookup = new Dictionary<int, RebuildSharedData.ClientTypes.ItemData>();
    }
}
