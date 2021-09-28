using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.Windows.Speech;

public class SaveManager
{
    public static readonly string defaultPath = (Application.persistentDataPath + "/saves/");
    public static readonly string defaultFolderPath = (Application.persistentDataPath + "/saves");
    public static readonly string saveExtension = ".save";

    public static bool Save(string saveName, object saveData)
    {
        BinaryFormatter formatter = GetBinaryFormatter();


        if (!Directory.Exists(defaultFolderPath))
        {
            Directory.CreateDirectory(defaultFolderPath);
        }

        string path = defaultPath + saveName + saveExtension;

        FileStream file = File.Create(path);
        
        formatter.Serialize(file, saveData);
        
        file.Close();
        
        return true;
    }

    public static object Load(string saveName)
    {
        if (!File.Exists(defaultPath + saveName + saveExtension))
        {
            return null;
        }

        BinaryFormatter formatter = GetBinaryFormatter();

        FileStream file = File.Open(defaultPath + saveName + saveExtension, FileMode.Open);

        try
        {
            object save = formatter.Deserialize(file);
            file.Close();
            return save;
        }
        catch
        {
            Debug.LogErrorFormat("Failed to load file at {0}", defaultPath + saveName);
            file.Close();
            return null;
        }
    }
    private static BinaryFormatter GetBinaryFormatter()
    {
        BinaryFormatter formatter = new BinaryFormatter();

        SurrogateSelector selector = new SurrogateSelector();

        Vector3SerializationSurrogate vector3Surrogate = new Vector3SerializationSurrogate();
        QuaternionSerializationSurrogate quaternionSurrogate = new QuaternionSerializationSurrogate();
        
        selector.AddSurrogate(typeof(Vector3), new StreamingContext(StreamingContextStates.All), vector3Surrogate);
        selector.AddSurrogate(typeof(Quaternion), new StreamingContext(StreamingContextStates.All), quaternionSurrogate);

        formatter.SurrogateSelector = selector;
        
        return formatter;
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
