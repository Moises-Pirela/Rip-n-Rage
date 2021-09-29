using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PlayerState : EntityState
{
    public Vector3 position;
    public Quaternion rotation;

    public InventoryComponent InventoryComponent;

    private void Start()
    {
        
    }
}
