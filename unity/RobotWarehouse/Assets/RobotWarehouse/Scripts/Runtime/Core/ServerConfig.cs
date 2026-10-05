using UnityEngine;

namespace RobotWarehouse.Core
{
    /// <summary>
    /// 서버 주소 설정. 에디터 메뉴 RobotWarehouse > 1. 서버 연결 에서 입력, 저장한다.
    /// Resources에 있어 Quest 빌드에도 그대로 들어가고, 앱은 시작하자마자 이 주소로 자동 연결한다.
    /// </summary>
    [CreateAssetMenu(menuName = "RobotWarehouse/Server Config", fileName = "ServerConfig")]
    public class ServerConfig : ScriptableObject
    {
        public const string ResourcePath = "ServerConfig";

        [Tooltip("서버 IP 또는 호스트 이름. 에디터 확인은 127.0.0.1, Quest는 노트북의 Wi-Fi IP")]
        public string host = "127.0.0.1";
        public int port = 8000;
        [Tooltip("앱 시작 시 자동 연결")]
        public bool autoConnect = true;
        [Header("관리자 모드")]
        [Tooltip("관리자 모드 진입에 PIN 입력을 요구 (시연·제출 빌드는 켜 둘 것)")]
        public bool requireAdminPin = true;
        [Tooltip("PIN 해시 (SHA-256). 에디터 메뉴 1. 서버 연결 → 관리자 PIN 에서 바꾼다. 비어 있으면 기본 PIN 0000")]
        public string adminPinHash = "";
        [Tooltip("PIN 자리 수 (4~6)")]
        public int adminPinLength = 4;


        static ServerConfig _cached;

        public static ServerConfig Load()
        {
            if (_cached == null) _cached = Resources.Load<ServerConfig>(ResourcePath);
            return _cached;
        }
    }
}
