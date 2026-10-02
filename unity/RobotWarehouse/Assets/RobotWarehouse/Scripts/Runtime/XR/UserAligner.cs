using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace RobotWarehouse.XR
{
    /// <summary>
    /// XR Origin을 씬에 놓아 둔 고정 위치·방향에 의존하지 않게 한다.
    /// 헤드셋 추적이 잡히면 사용자의 '실제 머리 위치·시선'을 기준으로 XR Origin을 옮기고 돌려,
    /// 사용자가 테이블 앞 standDistance 거리에서 테이블을 정면으로 보게 맞춘다.
    /// Quest 재중심(Oculus 버튼 길게) 때도 다시 맞춘다.
    /// </summary>
    public static class UserAligner
    {
        /// <summary>
        /// 머리 추적이 실제로 카메라에 반영됐는지. 헤드셋이 없으면 true(에디터 확인용).
        /// Link 연결만 되고 헤드셋을 쓰지 않은 상태에서는 카메라가 (0,0,0)에 머물러 false.
        /// </summary>
        public static bool HeadReady(XROrigin origin)
        {
            if (!XRSettings.isDeviceActive) return true;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool tracked = head.isValid && head.TryGetFeatureValue(CommonUsages.isTracked, out var t) && t;
            bool posed = origin != null && origin.Camera != null && origin.Camera.transform.localPosition.sqrMagnitude > 1e-4f;
            return tracked && posed;
        }

        const string SafetyFloorName = "RW_SafetyFloor";

        /// <summary>
        /// XR Origin 발밑 높이에 두께 있는 보이지 않는 바닥을 깐다.
        /// VR 템플릿의 Gravity 이동은 바닥 충돌체가 없거나 얇은 평면(Plane)뿐이면 XR Origin을 떨어뜨릴 수 있다.
        /// </summary>
        public static void EnsureGround(XROrigin origin)
        {
            if (origin == null) return;
            var p = origin.transform.position;
            if (GameObject.Find(SafetyFloorName) != null) return;
            var floor = new GameObject(SafetyFloorName);
            floor.transform.position = new Vector3(p.x, p.y - 0.1f, p.z);
            var box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(400f, 0.2f, 400f);
            Debug.Log($"[RobotWarehouse] 보이지 않는 안전 바닥을 만들었습니다 (윗면 y={p.y:0.00})");
        }

        /// <summary>
        /// XR Origin 순간 이동. CharacterController는 끄지 않는다(템플릿 이동 기능이 같은 프레임에 Move를 부르면
        /// "Move called on inactive controller" 오류가 나기 때문). 대신 이동 뒤 물리 위치를 바로 동기화한다.
        /// </summary>
        public static void Teleport(XROrigin origin, Action move)
        {
            move();
            Physics.SyncTransforms();
        }

        public static XROrigin FindOrigin()
        {
            var o = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            return o;
        }

        /// <summary>
        /// 사용자를 target 앞 standDistance 위치로 옮기고 target을 바라보게 한다.
        /// approachFrom: 사용자가 설 방향(테이블 중심 → 사용자, 수평). 0이면 현재 사용자 쪽.
        /// </summary>
        public static bool Align(XROrigin origin, Vector3 target, float standDistance, Vector3 approachFrom = default)
        {
            if (origin == null || origin.Camera == null) return false;
            var cam = origin.Camera.transform;

            var dir = Flat(approachFrom);
            if (dir.sqrMagnitude < 1e-4f) dir = Flat(cam.position - target);
            if (dir.sqrMagnitude < 1e-4f) dir = Flat(-cam.forward);
            dir.Normalize();

            Teleport(origin, () =>
            {
                float originY = origin.transform.position.y;

                // 1) 위치: 카메라를 테이블 앞 지점으로 (수평 이동만, 높이는 그대로)
                var stand = new Vector3(target.x, cam.position.y, target.z) + dir * standDistance;
                origin.MoveCameraToWorldLocation(stand);

                // 2) 방향: 시선(수평)이 테이블을 향하도록 카메라를 축으로 회전
                var look = Flat(target - cam.position);
                var fwd = Flat(cam.forward);
                if (look.sqrMagnitude > 1e-4f && fwd.sqrMagnitude > 1e-4f)
                    origin.RotateAroundCameraUsingOriginUp(Vector3.SignedAngle(fwd, look, Vector3.up));

                var op = origin.transform.position;
                origin.transform.position = new Vector3(op.x, originY, op.z);
            });
            return true;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static readonly List<XRInputSubsystem> _subsystems = new List<XRInputSubsystem>();

        /// <summary>Quest 재중심 이벤트 구독. 반환한 Action을 호출하면 구독 해제.</summary>
        public static Action SubscribeRecenter(Action onRecenter)
        {
            _subsystems.Clear();
            SubsystemManager.GetSubsystems(_subsystems);
            Action<XRInputSubsystem> handler = _ => onRecenter?.Invoke();
            var subs = new List<XRInputSubsystem>(_subsystems);
            foreach (var s in subs) s.trackingOriginUpdated += handler;
            return () => { foreach (var s in subs) s.trackingOriginUpdated -= handler; };
        }
    }
}
