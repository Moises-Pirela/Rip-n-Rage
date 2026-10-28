using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// Ported from Assets/_Game/Scripts/SaveSystem/SaveManager.cs.
// BinaryFormatter (and the Vector3/Quaternion serialization surrogates it needed) is obsolete and
// disabled on modern .NET, so saves are now written as JSON with System.Text.Json. Vector3 and
// Quaternion serialize naturally because their components are public fields.
public class SaveManager
{
    public static readonly string defaultPath = (Application.persistentDataPath + "/saves/");
    public static readonly string defaultFolderPath = (Application.persistentDataPath + "/saves");
    public static readonly string saveExtension = ".save";

    public static System.Action OnLoadData;

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        IncludeFields = true,
        WriteIndented = true,
    };

    public static bool Save(string saveName, object saveData)
    {
        if (!Directory.Exists(defaultFolderPath))
        {
            Directory.CreateDirectory(defaultFolderPath);
        }

        string path = defaultPath + saveName + saveExtension;

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(saveData, saveData.GetType(), Options));
            return true;
        }
        catch (Exception e)
        {
            Debug.LogErrorFormat("Failed to save file at {0}: {1}", path, e.Message);
            return false;
        }
    }

    public static object Load(string saveName)
    {
        if (!File.Exists(defaultPath + saveName + saveExtension))
        {
            return null;
        }

        Debug.Log(defaultPath);

        try
        {
            return JsonSerializer.Deserialize<SaveData>(File.ReadAllText(defaultPath + saveName + saveExtension), Options);
        }
        catch
        {
            Debug.LogErrorFormat("Failed to load file at {0}", defaultPath + saveName);
            return null;
        }
    }
    
    public static string[] GetLoadFiles()
    {
        if (!Directory.Exists(defaultPath))
        {
            Directory.CreateDirectory(defaultPath);
        }

        return Directory.GetFiles(defaultPath);
    }
}
