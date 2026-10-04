using RobotWarehouse.XR;
using UnityEngine;

namespace RobotWarehouse.UI
{
    /// <summary>
    /// VR 패널 묶음 (IR-06). 로직은 AppController가 연결한다.
    /// - User: 사용자 작업 패널 한 장 (단계별 화면 S01~S08)
    /// - Dev: 관리자·디버그 패널 (서버·오프라인 재생·보기 전환·예시 문장·로그, 기본 숨김)
    /// </summary>
    public class MainPanels
    {
        public UserPanel User;
        public DevPanel Dev;

        public void Build(Transform parent)
        {
            User = new UserPanel();
            User.Build(parent);
            Dev = new DevPanel();
            Dev.Build(parent);
            Dev.SetVisible(false);
            foreach (var c in User.Canvas.GetComponentsInChildren<Canvas>(true))
                if (c.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null) XRSupport.ConfigureCanvas(c);
            XRSupport.ConfigureCanvas(Dev.Canvas);
        }

        public void RefreshRaycasters()
        {
            foreach (var c in User.Canvas.GetComponentsInChildren<Canvas>(true))
                if (c.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null) XRSupport.RefreshRaycasters(c);
            XRSupport.RefreshRaycasters(Dev.Canvas);
        }

        /// <summary>관리자 패널 로그 (사용자에게는 보이지 않음).</summary>
        public void Log(string line) => Dev.Log(line);
    }
}
