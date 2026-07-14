using UnityEngine;

namespace Game.Debate
{
    [DefaultExecutionOrder(10000)]
    public class RefereeLabelBillboard : MonoBehaviour
    {
        private Transform _followTarget;
        private Vector3 _followLocalPosition;

        public void Follow(Transform target, Vector3 localPosition)
        {
            _followTarget = target;
            _followLocalPosition = localPosition;
        }

        private void LateUpdate()
        {
            if (_followTarget != null)
            {
                transform.position = _followTarget.TransformPoint(_followLocalPosition);
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            transform.rotation = mainCamera.transform.rotation;
        }
    }
}
