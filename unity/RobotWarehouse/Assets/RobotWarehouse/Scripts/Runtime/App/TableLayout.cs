using UnityEngine;

namespace RobotWarehouse.App
{
    /// <summary>
    /// 씬의 테이블을 기준으로 미니어처 창고·패널 위치를 계산한다.
    /// 테이블을 옮기거나 돌려도 창고는 테이블 윗면에, 패널은 테이블 양옆(사용자 쪽)에 놓인다.
    /// </summary>
    public static class TableLayout
    {
        public struct Top
        {
            public Vector3 center;     // 윗면 중앙 (월드)
            public Quaternion yaw;     // 테이블 방향 (Y축 회전만)
            public Vector2 size;       // x: 테이블 좌우 길이, y: 앞뒤 길이 (m)
        }

        public static bool TryGetTop(GameObject table, out Top top)
        {
            top = default;
            if (table == null) return false;
            var mf = table.GetComponentInChildren<MeshFilter>(true);
            if (mf != null && mf.sharedMesh != null)
            {
                var tr = mf.transform;
                var b = mf.sharedMesh.bounds;
                top.center = tr.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
                top.yaw = Quaternion.Euler(0f, tr.eulerAngles.y, 0f);
                var s = tr.lossyScale;
                top.size = new Vector2(Mathf.Abs(b.size.x * s.x), Mathf.Abs(b.size.z * s.z));
                return true;
            }
            var r = table.GetComponentInChildren<Renderer>(true);
            if (r == null) return false;
            var wb = r.bounds;
            top.center = new Vector3(wb.center.x, wb.max.y, wb.center.z);
            top.yaw = Quaternion.identity;
            top.size = new Vector2(wb.size.x, wb.size.z);
            return true;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>
        /// 창고 방향 선택: 테이블 축에 맞춘 4방향 중
        /// (1) 창고 긴 변이 테이블 긴 변과 나란하고 (2) 창고 북쪽(+y)이 사용자 반대쪽을 향하는 것.
        /// </summary>
        public static Quaternion ChooseMapRotation(Top top, float mapW, float mapH, Vector3 viewer)
        {
            var away = Flat(top.center - viewer);
            if (away.sqrMagnitude < 1e-4f) away = top.yaw * Vector3.forward;
            away.Normalize();
            bool mapWide = mapW >= mapH;
            bool tableWide = top.size.x >= top.size.y;
            Quaternion best = top.yaw;
            float bestScore = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                var q = top.yaw * Quaternion.Euler(0f, 90f * k, 0f);
                bool mapXAlongTableX = Mathf.Abs(Vector3.Dot(q * Vector3.right, top.yaw * Vector3.right)) > 0.5f;
                bool longAligned = mapXAlongTableX ? mapWide == tableWide : mapWide != tableWide;
                float score = (longAligned ? 2f : 0f) + Vector3.Dot(q * Vector3.forward, away);
                if (score > bestScore) { bestScore = score; best = q; }
            }
            return best;
        }

        /// <summary>창고 전체(mapW × mapH m)가 테이블 윗면의 margin 비율 안에 들어가는 축척.</summary>
        public static float FitScale(Top top, Quaternion mapRot, float mapW, float mapH, float margin)
        {
            bool mapXAlongTableX = Mathf.Abs(Vector3.Dot(mapRot * Vector3.right, top.yaw * Vector3.right)) > 0.5f;
            float availX = mapXAlongTableX ? top.size.x : top.size.y;
            float availZ = mapXAlongTableX ? top.size.y : top.size.x;
            return Mathf.Min(availX * margin / Mathf.Max(0.01f, mapW), availZ * margin / Mathf.Max(0.01f, mapH));
        }

        /// <summary>
        /// 테이블 양옆, 사용자 쪽으로 약간 나온 위치에 패널 두 장 (패널 피벗은 아래 가운데).
        /// 오른쪽 = 사용자 작업 패널(사용자 시점 오른쪽 아래), 왼쪽 = 관리자 패널.
        /// 패널은 사용자 눈을 향하도록 위아래로도 살짝 기울인다 (최대 20°).
        /// </summary>
        public static void PlacePanels(Top top, Vector3 viewer, float floorY, float panelBottom, float leftWidth, float rightWidth,
            float rightHeight, Transform left, Transform right)
        {
            var toUser = Flat(viewer - top.center);
            if (toUser.sqrMagnitude < 1e-4f) toUser = top.yaw * Vector3.back;
            toUser.Normalize();
            var viewRight = Vector3.Cross(Vector3.up, -toUser);   // 사용자가 테이블을 볼 때 오른쪽
            float half = Mathf.Max(top.size.x, top.size.y) * 0.5f;
            var basePos = new Vector3(top.center.x, floorY + panelBottom, top.center.z) + toUser * 0.45f;

            Place(left, basePos - viewRight * (half + leftWidth * 0.5f + 0.08f), viewer, rightHeight);
            Place(right, basePos + viewRight * (half * 0.85f + rightWidth * 0.5f), viewer, rightHeight);
        }

        /// <summary>pos = 패널 아래 가운데. 패널 가운데가 사용자 눈을 향하게 돌린다.</summary>
        public static void Place(Transform panel, Vector3 pos, Vector3 viewer, float panelHeight)
        {
            if (panel == null) return;
            panel.position = pos;
            var look = pos + Vector3.up * panelHeight * 0.5f - viewer;
            var flat = Flat(look);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            float pitch = Mathf.Clamp(Mathf.Atan2(-look.y, flat.magnitude) * Mathf.Rad2Deg, 0f, 20f);
            panel.rotation = Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
