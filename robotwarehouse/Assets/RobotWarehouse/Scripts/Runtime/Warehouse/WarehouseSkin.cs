using UnityEngine;

namespace RobotWarehouse.Warehouse
{
    /// <summary>
    /// 3D 에셋 외형 설정 (Unity Warehouse 에셋을 URP로 변환한 프리팹).
    /// 에디터 메뉴 RobotWarehouse > 2. 창고 에셋 적용 이 만들고 채운다. 없으면 기본 도형으로 그린다.
    /// 프리팹 피벗은 변환 도구가 '바닥 중앙'으로 맞춰 두고, 크기(size)는 그때 잰 값이다.
    /// </summary>
    [CreateAssetMenu(menuName = "RobotWarehouse/Warehouse Skin", fileName = "WarehouseSkin")]
    public class WarehouseSkin : ScriptableObject
    {
        public const string ResourcePath = "WarehouseSkin";

        [Tooltip("끄면 기본 도형(박스)으로 그림")] public bool useSkin = true;

        [Header("랙")]
        [Tooltip("켜면(기본) 철제 랙 에셋 모델을 쓴다. 끄면 가벼운 기본 선반 — Quest 프레임이 부족할 때")]
        public bool useAssetShelf = true;
        public GameObject shelfPrefab;
        public Vector3 shelfSize = Vector3.one;
        [Tooltip("랙 1단 높이(m). 랙 높이 = 단 수 × 이 값")] public float rackLevelHeight = 0.7f;
        [Tooltip("랙 칸에 상자를 채우는 비율(0~1). 성능이 부족하면 낮춤")]
        [Range(0f, 1f)] public float boxFill = 0.5f;

        [Header("상자·파레트")]
        public GameObject[] boxPrefabs;
        public Vector3 boxSize = Vector3.one * 0.5f;
        public GameObject palletPrefab;
        public Vector3 palletSize = Vector3.one;

        [Header("로봇")]
        public GameObject robotPrefab;
        public Vector3 robotSize = Vector3.one;
        [Tooltip("모델 정면이 +Z가 아니면 보정 각도(도)")] public float robotYawOffset = 0f;
        [Tooltip("로봇이 칸에서 차지하는 비율")] [Range(0.3f, 1f)] public float robotFootprint = 0.8f;

        static WarehouseSkin _cached;
        static bool _loaded;

        /// <summary>사용할 수 있는 스킨. 없거나 꺼져 있으면 null.</summary>
        public static WarehouseSkin Active
        {
            get
            {
                if (!_loaded)
                {
                    _cached = Resources.Load<WarehouseSkin>(ResourcePath);
                    _loaded = true;
                }
                return _cached != null && _cached.useSkin ? _cached : null;
            }
        }

        public bool HasShelf => shelfPrefab != null && shelfSize.x > 0.01f && shelfSize.z > 0.01f;
        public bool HasRobot => robotPrefab != null && robotSize.x > 0.01f && robotSize.z > 0.01f;
        public bool HasBoxes => boxPrefabs != null && boxPrefabs.Length > 0 && boxPrefabs[0] != null;
    }
}
