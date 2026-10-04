using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RobotWarehouse.XR
{
    /// <summary>
    /// VR 템플릿(XRI 3.x)과 연결하는 도우미.
    /// - World Space 캔버스에 TrackedDeviceGraphicRaycaster를 붙여 컨트롤러 레이로 UI를 누른다.
    /// - 헤드셋이 없을 때(에디터)는 GraphicRaycaster로 마우스 클릭을 받는다.
    /// 템플릿의 EventSystem(XRUIInputModule)이 둘 다 처리한다.
    /// </summary>
    public static class XRSupport
    {
        public static bool XRActive => XRSettings.isDeviceActive;

        public static void ConfigureCanvas(Canvas canvas)
        {
            if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            canvas.worldCamera = Camera.main;
            RefreshRaycasters(canvas);
        }

        /// <summary>XR 장치가 켜져 있으면 마우스용 GraphicRaycaster를 끈다 (레이 중복 방지).</summary>
        public static void RefreshRaycasters(Canvas canvas)
        {
            if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            // TrackedDeviceGraphicRaycaster는 GraphicRaycaster 상속이 아니므로 여기서 걸리지 않는다.
            foreach (var gr in canvas.GetComponents<GraphicRaycaster>())
                gr.enabled = !XRActive;
        }

        /// <summary>오른손 컨트롤러 버튼 상태 (A = primaryButton, B = secondaryButton).</summary>
        public static bool RightButton(InputFeatureUsage<bool> usage)
        {
            var d = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            return d.isValid && d.TryGetFeatureValue(usage, out var v) && v;
        }

        /// <summary>왼손 컨트롤러 버튼 상태 (X = primaryButton, Y = secondaryButton).</summary>
        public static bool LeftButton(InputFeatureUsage<bool> usage)
        {
            var d = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            return d.isValid && d.TryGetFeatureValue(usage, out var v) && v;
        }
    }
}
