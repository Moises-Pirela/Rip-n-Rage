using System;

public interface IProjectileSpawner
{
    public Action OnProjectileHit {get;set;}

    public void OnProjectileHit_Implementation();
}