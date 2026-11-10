using System.Collections;
using System.Collections.Generic;

public class HeadBob : MonoBehaviour
{
    [Range(0,100)]
    [SerializeField] public float frequency;

    [Range(0,100)]
    [SerializeField] public float waveLength;

    [Range(0,100)]
    [SerializeField] public float swayAmount;

    [Range(0,100)]
    [SerializeField] public float swaySpeed;


    [SerializeField] public FPSController controller;

    private Vector3 startPos;
    private float timer;
    /// <summary>Vertical offset the slide crouch asks for; taken from the player's PlayerMotor component.</summary>
    public float heightOffset;

    protected override void Start()
    {
        startPos = transform.localPosition;
    }

    protected override void Update()
    {
        if (controller == null) return;
        heightOffset = controller.CameraHeightOffset;
        if (!controller.IsGrounded) return;

        Vector2 move = Application.Current?.World != null
            ? Application.Current.World.GetSingleton<RipNRage.Engine.Ecs.Input.InputState>().Move
            : Vector2.zero;
        if (move.y != 0 || move.x != 0)
        {
            timer += Time.deltaTime * frequency;
            transform.localPosition = new Vector3(transform.localPosition.x, startPos.y + heightOffset + Mathf.Sin(timer) * waveLength, transform.localPosition.z);
        }
        else
        {
            timer = 0;
            transform.localPosition = new Vector3(transform.localPosition.x, Mathf.Lerp(transform.localPosition.y, startPos.y + heightOffset,
            Time.deltaTime * frequency), transform.localPosition.z);
        }
    }
}
