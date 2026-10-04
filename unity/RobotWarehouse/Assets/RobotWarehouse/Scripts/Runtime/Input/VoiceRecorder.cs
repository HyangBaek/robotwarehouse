using System;
using RobotWarehouse.Core;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace RobotWarehouse.InputModule
{
    /// <summary>
    /// 누르고 말하기 녹음 (SC-02). Microphone API -> WAV 바이트.
    /// 녹음 상한 15초. 너무 짧거나 조용하면 서버로 보내지 않는다.
    /// </summary>
    public class VoiceRecorder : MonoBehaviour
    {
        public bool IsRecording { get; private set; }
        public float RecordingSeconds => IsRecording ? Time.time - _startTime : 0f;

        /// <summary>녹음이 끝나면 WAV 바이트 전달. 실패하면 null과 사유.</summary>
        public event Action<byte[], string> OnRecorded;

        AudioClip _clip;
        string _device;
        float _startTime;

        void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                Permission.RequestUserPermission(Permission.Microphone);
#endif
        }

        public bool HasMicrophone => Microphone.devices != null && Microphone.devices.Length > 0;

        public void Begin()
        {
            if (IsRecording) return;
            if (!HasMicrophone)
            {
                OnRecorded?.Invoke(null, "마이크를 찾지 못했어요. 텍스트로 입력해 주세요");
                return;
            }
            _device = Microphone.devices[0];
            _clip = Microphone.Start(_device, false, AppConfig.MaxRecordSeconds, AppConfig.RecordSampleRate);
            _startTime = Time.time;
            IsRecording = true;
        }

        public void End()
        {
            if (!IsRecording) return;
            IsRecording = false;
            int pos = Microphone.GetPosition(_device);
            Microphone.End(_device);
            if (_clip == null) { OnRecorded?.Invoke(null, "녹음에 실패했어요"); return; }
            if (pos <= 0) pos = _clip.samples;   // 상한까지 녹음된 경우

            var samples = new float[pos * _clip.channels];
            _clip.GetData(samples, 0);
            Destroy(_clip);
            _clip = null;

            if (pos < AppConfig.RecordSampleRate / 2)
            {
                OnRecorded?.Invoke(null, "녹음이 너무 짧아요. 버튼을 누른 채로 말해 주세요");
                return;
            }
            if (WavEncoder.Rms(samples) < 0.002f)
            {
                OnRecorded?.Invoke(null, "잘 못 들었어요. 다시 말하거나 입력해 주세요");
                return;
            }
            OnRecorded?.Invoke(WavEncoder.Encode(samples, AppConfig.RecordSampleRate, 1), null);
        }

        void Update()
        {
            // 상한 도달 시 자동 종료
            if (IsRecording && RecordingSeconds >= AppConfig.MaxRecordSeconds) End();
        }
    }
}
