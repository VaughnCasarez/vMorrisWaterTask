using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Suppresses optional Meta Project Setup warnings that are irrelevant
/// when Platform SDK APIs are not used in this project.
/// </summary>
[InitializeOnLoad]
public static class MetaProjectSetupWarningSuppressor
{
    private static readonly string[] WarningSnippets =
    {
        "Complete a Data Use Checkup to meet DUC policy requirements",
        "Set up the application ID and the package name"
    };

    static MetaProjectSetupWarningSuppressor()
    {
        EditorApplication.delayCall += ApplyIgnores;
    }

    private static void ApplyIgnores()
    {
        try
        {
            var ovrProjectSetupType = Type.GetType("OVRProjectSetup");
            if (ovrProjectSetupType == null)
            {
                return;
            }

            var getTasksMethod = ovrProjectSetupType.GetMethod(
                "GetTasks",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (getTasksMethod == null)
            {
                return;
            }

            var changed = 0;
            changed += IgnoreForBuildTarget(getTasksMethod, BuildTargetGroup.Android);
            changed += IgnoreForBuildTarget(getTasksMethod, BuildTargetGroup.Standalone);

            if (changed > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"Meta Project Setup: ignored {changed} optional Platform SDK warnings.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"MetaProjectSetupWarningSuppressor failed: {ex.Message}");
        }
    }

    private static int IgnoreForBuildTarget(MethodInfo getTasksMethod, BuildTargetGroup targetGroup)
    {
        var changed = 0;
        var tasks = getTasksMethod.Invoke(null, new object[] { targetGroup }) as IEnumerable;
        if (tasks == null)
        {
            return changed;
        }

        foreach (var task in tasks)
        {
            if (task == null)
            {
                continue;
            }

            if (!IsMatchingWarning(task, targetGroup))
            {
                continue;
            }

            var setIgnoredMethod = task.GetType().GetMethod(
                "SetIgnored",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (setIgnoredMethod == null)
            {
                continue;
            }

            var isIgnoredMethod = task.GetType().GetMethod(
                "IsIgnored",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var isIgnored = false;
            if (isIgnoredMethod != null)
            {
                var value = isIgnoredMethod.Invoke(task, new object[] { targetGroup });
                if (value is bool b)
                {
                    isIgnored = b;
                }
            }

            if (!isIgnored)
            {
                setIgnoredMethod.Invoke(task, new object[] { targetGroup, true });
                changed++;
            }
        }

        return changed;
    }

    private static bool IsMatchingWarning(object task, BuildTargetGroup targetGroup)
    {
        var messageProperty = task.GetType().GetProperty(
            "Message",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (messageProperty == null)
        {
            return false;
        }

        var messageWrapper = messageProperty.GetValue(task);
        if (messageWrapper == null)
        {
            return false;
        }

        var getValueMethod = messageWrapper.GetType().GetMethod(
            "GetValue",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(BuildTargetGroup) },
            null);

        if (getValueMethod == null)
        {
            return false;
        }

        var messageObj = getValueMethod.Invoke(messageWrapper, new object[] { targetGroup });
        if (messageObj is not string message)
        {
            return false;
        }

        foreach (var snippet in WarningSnippets)
        {
            if (message.IndexOf(snippet, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
