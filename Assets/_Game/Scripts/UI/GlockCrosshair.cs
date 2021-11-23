using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEditor.Recorder;
using UnityEngine;
using UnityEngine.UI;

public class GlockCrosshair : MonoBehaviour, ICrosshair
{
    public Image CrosshairImage { get; set; }
    public Animator CrosshairAnimator { get; set; }

    [SerializeField]private Image innerCrosshairImage;

    [SerializeField]private Vector2 onHitSize;
    [SerializeField]private Vector2 regularSize;

    private Coroutine _resetCrosshairStateCoroutine;
    
    private void Awake()
    {
        CrosshairImage = GetComponent<Image>();
        CrosshairAnimator = GetComponent<Animator>();

        CrosshairImage.rectTransform.sizeDelta = regularSize;
        innerCrosshairImage.rectTransform.sizeDelta = new Vector2(5, 5);
    }

    public void OnShoot()
    {
        CrosshairImage.rectTransform.DOComplete();
        CrosshairImage.rectTransform.sizeDelta = regularSize;
        CrosshairImage.rectTransform.DOSizeDelta(onHitSize, 0.25f, false).SetEase(Ease.OutSine).onComplete += OnReset;
    }

    public void OnHit()
    {
        innerCrosshairImage.rectTransform.DOComplete();
        innerCrosshairImage.rectTransform.sizeDelta = new Vector2(5,5);
        innerCrosshairImage.rectTransform.DOSizeDelta(new Vector2(10,10), 0.25f, false).SetEase(Ease.OutSine).onComplete += OnResetHitmarker;
       
    }

    public void OnReset()
    {
        CrosshairImage.rectTransform.DOSizeDelta(regularSize, 0.25f, false);
    }

    public void OnResetHitmarker()
    {
        innerCrosshairImage.rectTransform.DOSizeDelta(new Vector2(5,5), 0.25f, false);
    }
}
