using UnityEngine;

namespace MSkill.Utility
{
    public class MSkillCameraSwitcher : MonoBehaviour
    {
        [SerializeField] private Camera[] cameras;
        [SerializeField] private KeyCode switchKey = KeyCode.C;

        private int currentIndex = 0;

        private void Start()
        {
            if (cameras == null || cameras.Length == 0)
            {
                return;
            }

            // 最初は 0 番目だけ有効
            for (int i = 0; i < cameras.Length; i++)
            {
                bool active = (i == currentIndex);
                if (cameras[i] != null)
                {
                    cameras[i].enabled = active;

                    var listener = cameras[i].GetComponent<AudioListener>();
                    if (listener != null)
                        listener.enabled = active;
                }
            }
        }

        private void Update()
        {
            if (cameras == null || cameras.Length == 0) return;

            if (Input.GetKeyDown(switchKey))
            {
                currentIndex++;
                if (currentIndex >= cameras.Length)
                    currentIndex = 0;

                for (int i = 0; i < cameras.Length; i++)
                {
                    bool active = (i == currentIndex);
                    if (cameras[i] != null)
                    {
                        cameras[i].enabled = active;

                        var listener = cameras[i].GetComponent<AudioListener>();
                        if (listener != null)
                            listener.enabled = active;
                    }
                }
            }
        }
    }
}
