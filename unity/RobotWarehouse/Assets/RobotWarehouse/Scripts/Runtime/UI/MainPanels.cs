using RobotWarehouse.XR;
using UnityEngine;

namespace RobotWarehouse.UI
{
    /// <summary>
    /// VR 패널 묶음 (IR-06). 로직은 AppController가 연결한다.
    /// - User: 사용자 작업 패널 한 장 (단계별 화면 S01~S08)
    /// - Dev: 관리자, 개발자 패널 (Dashboard, Connection, Agent, Simulation, Logs, Scenario, Performance). 둘 중 하나만 보임
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
            foreach (var root in new[] { User.Canvas, Dev.Canvas })
                foreach (var c in root.GetComponentsInChildren<Canvas>(true))
                    if (c.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null) XRSupport.ConfigureCanvas(c);
        }

        public void RefreshRaycasters()
        {
            foreach (var root in new[] { User.Canvas, Dev.Canvas })
                foreach (var c in root.GetComponentsInChildren<Canvas>(true))
                    if (c.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null) XRSupport.RefreshRaycasters(c);
        }
    }
}
