using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HeadBob : MonoBehaviour
{
    [Range(0,100)]
    [SerializeField] private float frequency;

    [Range(0,100)]
    [SerializeField] private float waveLength;

    [Range(0,100)]
    [SerializeField] private float swayAmount;

    [Range(0,100)]
    [SerializeField] private float swaySpeed;


    [SerializeField] private FPSController controller;

    private Vector3 startPos;
    private float timer;

    private void Start()
    {
        startPos = transform.localPosition;
    }

    private void Update()
    {
        if (!controller._inputActions.IsGrounded) return;

        if (controller._inputActions.MovementInput.y != 0 || controller._inputActions.MovementInput.x != 0)
        {
            timer += Time.deltaTime * frequency;
            transform.localPosition = new Vector3(transform.localPosition.x, startPos.y + Mathf.Sin(timer) * waveLength, transform.localPosition.z);
        }
        else
        {
            timer = 0;
            transform.localPosition = new Vector3(transform.localPosition.x, Mathf.Lerp(transform.localPosition.y, startPos.y,
            Time.deltaTime * frequency), transform.localPosition.z);
        }
    }
}
