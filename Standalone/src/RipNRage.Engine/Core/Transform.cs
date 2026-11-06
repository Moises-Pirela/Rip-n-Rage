using System.Collections;
using System.Collections.Generic;

namespace RipNRage.Engine
{
    /// <summary>
    /// Hierarchical transform with the UnityEngine.Transform API the game scripts use
    /// (local/world position & rotation, euler angles, forward/right/up, parenting, LookAt).
    /// </summary>
    public sealed class Transform : Component, IEnumerable<Transform>
    {
        private Vector3 _localPosition = Vector3.zero;
        private Quaternion _localRotation = Quaternion.identity;
        private Vector3 _localScale = Vector3.one;
        private Transform _parent;
        private readonly List<Transform> _children = new List<Transform>();

        // World-matrix cache. Every local change takes a fresh, globally increasing stamp; a transform's world stamp
        // is the newest stamp on its parent chain, so a cached matrix is valid exactly while nothing above changed.
        private static long _stampCounter;
        private long _localStamp = ++_stampCounter;
        private long _cachedStamp = -1;
        private Matrix4x4 _cachedLocalToWorld, _cachedWorldToLocal;
        private bool _hasWorldToLocal;
        private void Touch() => _localStamp = ++_stampCounter;
        internal long WorldStamp { get { long s = _localStamp; for (var p = _parent; p != null; p = p._parent) if (p._localStamp > s) s = p._localStamp; return s; } }

        internal Transform(GameObject go) { gameObject = go; }

        public Vector3 localPosition { get => _localPosition; set { _localPosition = value; Touch(); } }
        public Quaternion localRotation { get => _localRotation; set { _localRotation = value; Touch(); } }
        public Vector3 localScale { get => _localScale; set { _localScale = value; Touch(); } }

        public Vector3 localEulerAngles
        {
            get => _localRotation.eulerAngles;
            set => localRotation = Quaternion.Euler(value);
        }

        public Vector3 position
        {
            get => _parent == null ? _localPosition : _parent.localToWorldMatrix.MultiplyPoint3x4(_localPosition);
            set { _localPosition = _parent == null ? value : _parent.worldToLocalMatrix.MultiplyPoint3x4(value); Touch(); }
        }

        public Quaternion rotation
        {
            get => _parent == null ? _localRotation : _parent.rotation * _localRotation;
            set { _localRotation = _parent == null ? value : Quaternion.Inverse(_parent.rotation) * value; Touch(); }
        }

        public Vector3 eulerAngles
        {
            get => rotation.eulerAngles;
            set => rotation = Quaternion.Euler(value);
        }

        public Vector3 lossyScale
        {
            get
            {
                if (_parent == null) return _localScale;
                return Vector3.Scale(_parent.lossyScale, _localScale);
            }
        }

        public Vector3 forward { get => rotation * Vector3.forward; set => rotation = Quaternion.LookRotation(value); }
        public Vector3 right => rotation * Vector3.right;
        public Vector3 up => rotation * Vector3.up;

        public Transform parent
        {
            get => _parent;
            set => SetParent(value, true);
        }

        public Transform root => _parent == null ? this : _parent.root;
        public int childCount => _children.Count;
        public Transform GetChild(int index) => _children[index];

        /// <summary>Reparents. With worldPositionStays the world pose is preserved (Unity default).</summary>
        public void SetParent(Transform newParent, bool worldPositionStays = true)
        {
            if (newParent == _parent) return;
            Vector3 worldPos = position;
            Quaternion worldRot = rotation;
            Vector3 worldScale = lossyScale;
            _parent?._children.Remove(this);
            _parent = newParent;
            _parent?._children.Add(this);
            Touch();
            if (worldPositionStays)
            {
                position = worldPos;
                rotation = worldRot;
                if (_parent != null)
                {
                    Vector3 ps = _parent.lossyScale;
                    _localScale = new Vector3(
                        ps.x != 0 ? worldScale.x / ps.x : worldScale.x,
                        ps.y != 0 ? worldScale.y / ps.y : worldScale.y,
                        ps.z != 0 ? worldScale.z / ps.z : worldScale.z);
                }
                else _localScale = worldScale;
                Touch();
            }
        }

        public Matrix4x4 localMatrix => Matrix4x4.TRS(_localPosition, _localRotation, _localScale);

        /// <summary>World matrix, cached until this transform or anything above it changes.</summary>
        public Matrix4x4 localToWorldMatrix
        {
            get
            {
                long stamp = WorldStamp;
                if (stamp != _cachedStamp)
                {
                    _cachedLocalToWorld = _parent == null ? localMatrix : _parent.localToWorldMatrix * localMatrix;
                    _cachedStamp = stamp; _hasWorldToLocal = false;
                }
                return _cachedLocalToWorld;
            }
        }

        public Matrix4x4 worldToLocalMatrix
        {
            get
            {
                Matrix4x4 l2w = localToWorldMatrix; // refreshes the cache stamp
                if (!_hasWorldToLocal) { _cachedWorldToLocal = l2w.inverse; _hasWorldToLocal = true; }
                return _cachedWorldToLocal;
            }
        }

        public Vector3 TransformPoint(Vector3 p) => localToWorldMatrix.MultiplyPoint3x4(p);
        public Vector3 InverseTransformPoint(Vector3 p) => worldToLocalMatrix.MultiplyPoint3x4(p);
        public Vector3 TransformDirection(Vector3 d) => rotation * d;
        public Vector3 InverseTransformDirection(Vector3 d) => Quaternion.Inverse(rotation) * d;

        public void Translate(Vector3 translation, Space relativeTo = Space.Self)
        {
            if (relativeTo == Space.World) position += translation;
            else position += TransformDirection(translation);
        }

        public void Rotate(Vector3 eulers, Space relativeTo = Space.Self)
        {
            Quaternion q = Quaternion.Euler(eulers);
            if (relativeTo == Space.Self) localRotation = localRotation * q;
            else rotation = q * rotation;
        }

        public void Rotate(float xAngle, float yAngle, float zAngle, Space relativeTo = Space.Self) =>
            Rotate(new Vector3(xAngle, yAngle, zAngle), relativeTo);

        public void LookAt(Vector3 worldPosition) => LookAt(worldPosition, Vector3.up);

        public void LookAt(Vector3 worldPosition, Vector3 worldUp)
        {
            Vector3 dir = worldPosition - position;
            if (dir.sqrMagnitude < 1E-08f) return;
            rotation = Quaternion.LookRotation(dir, worldUp);
        }

        public void LookAt(Transform target) => LookAt(target.position);

        public Transform Find(string name)
        {
            foreach (var c in _children)
                if (c.gameObject.name == name) return c;
            return null;
        }

        public bool IsChildOf(Transform t)
        {
            for (var p = this; p != null; p = p._parent)
                if (p == t) return true;
            return false;
        }

        internal IReadOnlyList<Transform> Children => _children;

        public IEnumerator<Transform> GetEnumerator() => _children.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public enum Space { World, Self }
}
