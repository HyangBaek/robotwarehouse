using System;
using System.Collections.Generic;
using RobotWarehouse.Core;
using RobotWarehouse.Data;
using RobotWarehouse.Warehouse;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace RobotWarehouse.XR
{
    /// <summary>
    /// VR에서 랙 옮기기 (SC-11, P2).
    /// 편집 모드를 켜면 랙 블록에 XRGrabInteractable을 붙인다. 놓으면 가장 가까운 칸에 스냅하고,
    /// 통로 칸이 아니면 원래 자리로 되돌린다. 이동 기록은 서버 POST /map/edit으로 보낸다.
    /// </summary>
    public class RackGrabEditor : MonoBehaviour
    {
        public WarehouseRenderer warehouse;
        public bool Editing { get; private set; }

        /// <summary>(rack_id, from, to) 이동 확정 시.</summary>
        public event Action<string, Vector2Int, Vector2Int> OnRackMoved;
        /// <summary>놓을 수 없는 칸에 놓았을 때 사유.</summary>
        public event Action<string> OnRejected;

        readonly List<Component> _added = new List<Component>();

        public void SetEditing(bool on)
        {
            if (on == Editing) return;
            Editing = on;
            if (on) Attach(); else Detach();
        }

        void Attach()
        {
            if (warehouse == null || warehouse.Map == null) return;
            foreach (var kv in warehouse.RackObjects)
            {
                var go = kv.Value;
                var rb = go.GetComponent<Rigidbody>();
                if (rb == null) rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                var grab = go.AddComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.throwOnDetach = false;
                grab.trackRotation = false;
                grab.useDynamicAttach = true;
                grab.selectExited.AddListener(OnReleased);
                _added.Add(grab);
                _added.Add(rb);
            }
        }

        void Detach()
        {
            // XRGrabInteractable을 먼저 지우고 Rigidbody를 지운다.
            foreach (var c in _added) if (c is XRGrabInteractable) Destroy(c);
            foreach (var c in _added) if (c is Rigidbody) Destroy(c);
            _added.Clear();
        }

        void OnReleased(SelectExitEventArgs args)
        {
            var go = args.interactableObject.transform.gameObject;
            var tag = go.GetComponent<RackTag>();
            if (tag == null || warehouse.Map == null) return;
            var from = tag.cell;
            var to = warehouse.WorldToCell(go.transform.position);
            float cs = warehouse.CellSize;

            if (to != from && RackPlacement.CanPlace(warehouse.Map, to.x, to.y) && warehouse.MoveRack(from, to))
            {
                OnRackMoved?.Invoke(tag.rackId, from, to);
            }
            else
            {
                if (to != from)
                    OnRejected?.Invoke($"({to.x},{to.y})에는 랙을 놓을 수 없어요 ({CellTypeNames.ToName(warehouse.Map.GetCell(to.x, to.y))})");
                to = from;
            }
            // 칸 중심·바닥 높이로 스냅
            float h = go.transform.localScale.y * 0.5f;
            go.transform.localPosition = GridCoord.CellToLocal(to.x, to.y, cs, h);
            go.transform.localRotation = Quaternion.identity;
        }

        void OnDisable() => SetEditing(false);
    }
}
