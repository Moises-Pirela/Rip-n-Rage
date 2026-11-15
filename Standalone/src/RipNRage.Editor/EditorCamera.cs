using RipNRage.Engine;
using RipNRage.Engine.Graphics;

namespace RipNRage.Editor
{
    /// <summary>Fly camera (9.6: a screen driving a camera entity): hold the right mouse button to look around,
    /// WASD/QE to move, Shift = fast, wheel = speed.</summary>
    public class EditorCamera : IUiScreen
    {
        public float yaw = 180f, pitch = 15f;
        public float speed = 12f;
        public float sensitivity = 0.12f;
        public Vector3 position = new Vector3(0f, 14f, 30f);
        public bool Flying { get; private set; }
        /// <summary>False while a panel owns the pointer: no fly-look, no WASD (the pose still applies).</summary>
        public bool InputEnabled = true;

        /// <summary>The camera's pose and projection (fov 70): rays and screen points for picking.</summary>
        public readonly CameraView View = new CameraView { fieldOfView = 70f, nearClipPlane = 0.1f, farClipPlane = 3000f };

        private RipNRage.Engine.Ecs.Entity _lens;

        public Quaternion Rotation => Quaternion.Euler(pitch, yaw, 0f);
        public Vector3 Forward => Rotation * Vector3.forward;
        public Vector3 Right => Rotation * Vector3.right;

        public void Update()
        {
            if (!InputEnabled) { Apply(); return; }
            bool rmb = Input.GetMouseButton(1);
            if (rmb != Flying)
            {
                Flying = rmb;
                Cursor.lockState = rmb ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !rmb;
            }
            if (Flying)
            {
                var d = Input.mouseDelta;
                yaw += d.x * sensitivity;
                pitch = Mathf.Clamp(pitch - d.y * sensitivity, -89f, 89f);
                float scroll = Input.mouseScrollDelta.y;
                if (scroll != 0f) speed = Mathf.Clamp(speed * (scroll > 0 ? 1.25f : 0.8f), 1f, 200f);
            }

            // WASD/QE move any time (Ctrl+key shortcuts excluded)
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
            {
                Vector3 move = Vector3.zero;
                if (Input.GetKey(KeyCode.W)) move += Forward;
                if (Input.GetKey(KeyCode.S)) move -= Forward;
                if (Input.GetKey(KeyCode.D)) move += Right;
                if (Input.GetKey(KeyCode.A)) move -= Right;
                if (Input.GetKey(KeyCode.E)) move += Vector3.up;
                if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
                float s = speed * (Input.GetKey(KeyCode.LeftShift) ? 4f : 1f);
                position += move * s * Time.unscaledDeltaTime;
            }
            Apply();
        }

        public void Apply()
        {
            View.Position = position;
            View.Rotation = Rotation;
            var w = Application.Current.World;
            if (_lens.IsNull || !w.IsAlive(_lens))
            {
                _lens = w.CreateEntity();
                w.Add(_lens, new RipNRage.Engine.Ecs.Transforms.LocalTransform { Position = position, Rotation = Rotation, Scale = Vector3.one });
                w.Add(_lens, new RipNRage.Engine.Ecs.Rendering.CameraLens { FieldOfView = View.fieldOfView, Near = View.nearClipPlane, Far = View.farClipPlane });
            }
            ref var lt = ref w.Get<RipNRage.Engine.Ecs.Transforms.LocalTransform>(_lens);
            lt.Position = position;
            lt.Rotation = Rotation;
        }

        public void LookAt(Vector3 target)
        {
            Vector3 d = target - position;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            Apply();
        }

        public void DrawUi(UiBatch ui) { }
        public void Close() { }
    }
}
