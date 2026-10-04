using UnityEngine;
using UnityEngine.InputSystem;

namespace RobotWarehouse.XR
{
    /// <summary>
    /// 헤드셋 없이 에디터에서 확인할 때 쓰는 이동 (XR 장치가 켜지면 아무것도 하지 않는다).
    /// WASD 이동, Q/E 하강, 상승, 마우스 오른쪽 버튼 드래그로 둘러보기. UI 클릭은 마우스 왼쪽.
    /// </summary>
    public class DesktopRigController : MonoBehaviour
    {
        [Tooltip("움직일 대상. 비우면 XR Origin(이 오브젝트)")] public Transform rig;
        [Tooltip("시선 회전 대상. 비우면 Camera.main")] public Transform head;
        public float moveSpeed = 2.5f;
        public float lookSpeed = 0.15f;

        float _pitch;

        void Start()
        {
            // 헤드셋 없이 실행하면 카메라가 바닥 높이에 있을 수 있어 눈높이로 올린다.
            if (XRSupport.XRActive) return;
            var h = head != null ? head : (Camera.main != null ? Camera.main.transform : null);
            // 리그(패널 포함)는 그대로 두고 카메라만 올린다.
            if (h != null && h.position.y < 0.5f) h.position += Vector3.up * (1.6f - h.position.y);
        }

        void Update()
        {
            if (XRSupport.XRActive) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;
            var r = rig != null ? rig : transform;
            var h = head != null ? head : (Camera.main != null ? Camera.main.transform : null);
            if (h == null) return;

            var fwd = Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(h.right, Vector3.up).normalized;
            var move = Vector3.zero;
            if (kb.wKey.isPressed) move += fwd;
            if (kb.sKey.isPressed) move -= fwd;
            if (kb.dKey.isPressed) move += right;
            if (kb.aKey.isPressed) move -= right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move -= Vector3.up;
            float speed = kb.leftShiftKey.isPressed ? moveSpeed * 3f : moveSpeed;
            r.position += move * speed * Time.deltaTime;

            if (mouse != null && mouse.rightButton.isPressed)
            {
                var d = mouse.delta.ReadValue() * lookSpeed;
                r.Rotate(Vector3.up, d.x, Space.World);
                _pitch = Mathf.Clamp(_pitch - d.y, -80f, 80f);
                h.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }
    }
}
