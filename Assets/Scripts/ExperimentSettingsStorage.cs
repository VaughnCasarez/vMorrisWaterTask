using System.IO;
using UnityEngine;

public static class ExperimentSettingsStorage
{
    private const string FolderName = "ExperimentSettings";

    public static string DirectoryPath
    {
        get { return Path.Combine(Application.streamingAssetsPath, FolderName); }
    }

    public static void EnsureDirectoryExists()
    {
        Directory.CreateDirectory(DirectoryPath);
    }

    public static string[] GetFiles()
    {
        EnsureDirectoryExists();
        return Directory.GetFiles(DirectoryPath, "*.json");
    }

    public static string GetFilePath(string fileName)
    {
        return Path.Combine(DirectoryPath, fileName);
    }

    public static void DeleteFile(string fileName)
    {
        string path = GetFilePath(fileName);
        if (File.Exists(path)) File.Delete(path);

        string metaPath = path + ".meta";
        if (File.Exists(metaPath)) File.Delete(metaPath);
    }
}