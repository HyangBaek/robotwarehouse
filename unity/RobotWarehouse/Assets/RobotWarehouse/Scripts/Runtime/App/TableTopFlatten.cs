using UnityEngine;

namespace RobotWarehouse.App
{
    /// <summary>
    /// 테이블 모델(Japan Office Table_02A) 가운데 케이블 덮개는 윗면보다 약 2cm 솟아 있다.
    /// TableLayout은 메쉬의 가장 높은 점을 윗면으로 보므로, 그대로 두면 미니어처 창고가 떠 보이거나
    /// 덮개가 창고 바닥을 뚫고 나온다. 실행할 때 윗면보다 높은 정점을 윗면 높이로 눌러 평평하게 만든다.
    /// 씬 파일은 원본 메쉬를 그대로 가리키고, 복사본만 바꾼다 (에디터 편집 상태에는 영향 없음).
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [DefaultExecutionOrder(-100)]   // AppController가 테이블 윗면을 읽기 전에
    public class TableTopFlatten : MonoBehaviour
    {
        [Tooltip("메쉬 기준 윗면 높이(m). 이보다 높은 정점은 이 높이로 내린다")]
        public float topHeight = 0.72f;

        Mesh _flat;

        void Awake()
        {
            var mf = GetComponent<MeshFilter>();
            var src = mf.sharedMesh;
            if (src == null || !src.isReadable) return;

            _flat = Instantiate(src);
            _flat.name = src.name + " (flat top)";
            var v = _flat.vertices;
            bool changed = false;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i].y > topHeight)
                {
                    v[i].y = topHeight;
                    changed = true;
                }
            }
            if (!changed)
            {
                Destroy(_flat);
                _flat = null;
                return;
            }
            _flat.vertices = v;
            _flat.RecalculateBounds();
            mf.sharedMesh = _flat;
        }

        void OnDestroy()
        {
            if (_flat != null) Destroy(_flat);
        }
    }
}
