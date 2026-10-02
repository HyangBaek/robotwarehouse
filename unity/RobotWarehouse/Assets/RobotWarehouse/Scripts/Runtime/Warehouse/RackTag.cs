using UnityEngine;

namespace RobotWarehouse.Warehouse
{
    /// <summary>랙 블록에 붙는 식별 정보.</summary>
    public class RackTag : MonoBehaviour
    {
        public string rackId;
        public Vector2Int cell;
    }
}
