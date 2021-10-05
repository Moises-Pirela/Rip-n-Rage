using System;
using UnityEngine;
using UnityEngine.Events;

public interface IProjectileSpawner
{
    public Action OnProjectileHit {get;set;}

    public void OnProjectileHit_Implementation();
}