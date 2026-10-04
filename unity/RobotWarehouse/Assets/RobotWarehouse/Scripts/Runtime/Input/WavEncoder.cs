using System;
using System.IO;
using System.Text;

namespace RobotWarehouse.InputModule
{
    /// <summary>float PCM -> 16bit 모노 WAV 바이트 (Whisper STT 업로드용).</summary>
    public static class WavEncoder
    {
        public static byte[] Encode(float[] samples, int sampleRate, int channels = 1)
        {
            int byteCount = samples.Length * 2;
            using (var ms = new MemoryStream(44 + byteCount))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + byteCount);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);                       // PCM
                w.Write((short)channels);
                w.Write(sampleRate);
                w.Write(sampleRate * channels * 2);      // byte rate
                w.Write((short)(channels * 2));          // block align
                w.Write((short)16);                      // 샘플당 비트 수
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(byteCount);
                foreach (var f in samples)
                {
                    float c = Math.Max(-1f, Math.Min(1f, f));
                    w.Write((short)(c * short.MaxValue));
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>RMS 음량. 무음 판정(EX-03 사전 차단)에 쓴다.</summary>
        public static float Rms(float[] samples)
        {
            if (samples == null || samples.Length == 0) return 0f;
            double sum = 0;
            foreach (var s in samples) sum += s * s;
            return (float)Math.Sqrt(sum / samples.Length);
        }
    }
}
