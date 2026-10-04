using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RobotWarehouse.UI
{
    /// <summary>누르고 있는 동안 동작하는 버튼 (녹음 버튼, SC-02).</summary>
    [RequireComponent(typeof(Image))]
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public event Action OnPress;
        public event Action OnRelease;
        public bool Held { get; private set; }
        public bool Interactable = true;

        Image _img;
        Color _normal;
        public Color heldColor = new Color(1f, 0.27f, 0.23f, 1f);   // visionOS 시스템 빨강

        void Awake()
        {
            _img = GetComponent<Image>();
            _normal = _img.color;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!Interactable || Held) return;
            Held = true;
            _img.color = heldColor;
            OnPress?.Invoke();
        }

        public void OnPointerUp(PointerEventData e) => Release();

        void OnDisable() => Release();

        void Release()
        {
            if (!Held) return;
            Held = false;
            if (_img != null) _img.color = _normal;
            OnRelease?.Invoke();
        }
    }
}
