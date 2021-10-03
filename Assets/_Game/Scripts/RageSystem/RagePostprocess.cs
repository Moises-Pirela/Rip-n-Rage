using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class RagePostprocess : MonoBehaviour
{
    public Volume enragedVolume;
    public Volume depressedVolume;

    private Coroutine _enragedTransition;
    private Coroutine _depressedTransition;

    public void OnEnterEnraged()
    {
        if (_enragedTransition != null)
            StopCoroutine(_enragedTransition);

        _enragedTransition = StartCoroutine(DoEnragedTransition());
    }
    
    public void OnEnterDepressed()
    {
        if (_depressedTransition != null)
            StopCoroutine(_depressedTransition);
        
        _depressedTransition = StartCoroutine(DoDepressedTransition());
    }

    public void OnEnterNormal()
    {
        StopAllCoroutines();
        enragedVolume.weight = 0;
        depressedVolume.weight = 0;
    }

    private IEnumerator DoEnragedTransition()
    {
        while (enragedVolume.weight < 0.5f)
        {
            enragedVolume.weight = Mathf.Lerp(enragedVolume.weight, 0.5f, 30 * Time.deltaTime);
            yield return new WaitForSeconds(0.01f);
        }
    }

    private IEnumerator DoDepressedTransition()
    {
        while (depressedVolume.weight < 0.5f)
        {
            depressedVolume.weight = Mathf.Lerp(depressedVolume.weight, 0.5f, 30 * Time.deltaTime);
            yield return new WaitForSeconds(0.01f);
        }
    }
}
