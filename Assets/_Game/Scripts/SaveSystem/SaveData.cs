using UnityEngine;
using  System;
using System.Collections;

[Serializable]
public class SaveData
{
    private static SaveData _current;

    public static SaveData Current
    {
        get
        {
            if (_current == null)
            {
                _current = new SaveData();
            }

            return _current;
        }
        set => _current = value;
        
    }

    public PlayerStateData playerStateData = new PlayerStateData(Vector3.zero, Quaternion.identity, 100, 100);
    
    public InventoryData inventoryData = new InventoryData(new [] 
    {
        120,  // Light
        240,  // Heavy
        64,  // Shell
        100, // Energy
        10   // Explosive
    }, 0);
}