using RipNRage.Engine;

namespace RipNRage.Editor
{
    /// <summary>Fly camera: hold the right mouse button to look around, WASD/QE to move, Shift = fast, wheel = speed.</summary>
    public class EditorCamera : Behaviour
    {
        public float yaw = 180f, pitch = 15f;
        public float speed = 12f;
        public float sensitivity = 0.12f;
        public bool Flying { get; private set; }

        protected override void Start()
        {
            Apply();
        }

        protected override void Update()
        {
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
            Apply();

            // WASD/QE move any time (Ctrl+key shortcuts excluded)
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
            {
                Vector3 move = Vector3.zero;
                if (Input.GetKey(KeyCode.W)) move += transform.forward;
                if (Input.GetKey(KeyCode.S)) move -= transform.forward;
                if (Input.GetKey(KeyCode.D)) move += transform.right;
                if (Input.GetKey(KeyCode.A)) move -= transform.right;
                if (Input.GetKey(KeyCode.E)) move += Vector3.up;
                if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
                float s = speed * (Input.GetKey(KeyCode.LeftShift) ? 4f : 1f);
                transform.position += move * s * Time.unscaledDeltaTime;
            }
        }

        public void Apply() => transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        public void LookAt(Vector3 target)
        {
            Vector3 d = target - transform.position;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            Apply();
        }
    }
}
