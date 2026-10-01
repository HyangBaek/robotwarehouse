using UnityEngine;

namespace MSkill.Water
{
    [RequireComponent(typeof(MeshFilter))]
    public class WaterSurfaceAnimator : MonoBehaviour
    {
        [Header("Wave Shape")]
        [SerializeField] private float amplitude = 0.02f;   // 波の高さ
        [SerializeField] private float noiseScale = 1.2f;   // 波の細かさ
        [SerializeField] private float speed = 1.0f;        // 時間の進み方

        [Header("Aeration Feel")]
        [SerializeField] private float secondarySpeed = 2.0f; // 2方向目のスピード

        [Header("Debug")]
        [SerializeField] private bool recalculateNormals = true;

        private Mesh mesh;
        private Vector3[] baseVerts;
        private Vector3[] deformVerts;

        // 一方向にならないためのランダム方向（インスタンスごとに固定）
        private Vector2 randomDir;

        private void Start()
        {
            var mf = GetComponent<MeshFilter>();

            // 共有メッシュを直接書き換えないように複製
            mesh = Instantiate(mf.sharedMesh);
            mf.mesh = mesh;

            baseVerts = mesh.vertices;
            deformVerts = new Vector3[baseVerts.Length];

            // 揺れの進行方向をランダムに決定（XZ平面内）
            randomDir = Random.insideUnitCircle.normalized;
        }

        private void Update()
        {
            float t = Time.time * speed;

            for (int i = 0; i < baseVerts.Length; i++)
            {
                Vector3 v = baseVerts[i];

                // ランダム方向を使ったPerlinノイズ入力
                float n1 = Mathf.PerlinNoise(
                    v.x * noiseScale + t * randomDir.x,
                    v.z * noiseScale + t * randomDir.y
                );

                float n2 = Mathf.PerlinNoise(
                    v.x * (noiseScale * 1.5f) - t * secondarySpeed * randomDir.y,
                    v.z * (noiseScale * 0.7f) + t * 0.5f * randomDir.x
                );

                // -1〜1 に変換してから振幅をかける
                float height = ((n1 + n2) - 1f) * amplitude * 2f;

                v.y = baseVerts[i].y + height;
                deformVerts[i] = v;
            }

            mesh.vertices = deformVerts;

            if (recalculateNormals)
            {
                mesh.RecalculateNormals();
            }
        }
    }
}
