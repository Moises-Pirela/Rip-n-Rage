using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HudController : MonoBehaviour
{
    [SerializeField] private Image rageMeterFill;
    public void Start()
    {
        RageManager.instance.onRageMeterChange += UpdateRageMeter;

        UpdateRageMeter();
    }

    private void UpdateRageMeter()
    {
        rageMeterFill.fillAmount = Mathf.Lerp(rageMeterFill.fillAmount, RageManager.instance.RageMeter, 1f);
    }
}
