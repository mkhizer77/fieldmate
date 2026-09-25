using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Fieldmate.XR;

/// <summary>
/// Horizon OS runtime permissions. The Meta OpenXR package adds them to the manifest but never requests them:
/// scene data (mesh, occlusion) needs <see cref="Scene"/>, passthrough camera images need <see cref="HeadsetCamera"/>.
/// </summary>
public static class QuestPermissions
{
    public const string Scene = "com.oculus.permission.USE_SCENE";
    public const string HeadsetCamera = "horizonos.permission.HEADSET_CAMERA";

    public static bool IsGranted(string permission)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(permission);
#else
        return true;
#endif
    }

    /// <summary>Requests every permission not yet granted. Calls <paramref name="onResult"/> once per permission.</summary>
    /// <remarks>Callback arguments: permission, granted, and whether the user was prompted in this call.</remarks>
    public static void Request(IReadOnlyList<string> permissions, Action<string, bool, bool> onResult)
    {
        var pending = new List<string>();
        foreach (var permission in permissions)
        {
            if (IsGranted(permission))
            {
                onResult(permission, true, false);
            }
            else
            {
                pending.Add(permission);
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += permission => onResult(permission, true, true);
        callbacks.PermissionDenied += permission => onResult(permission, false, true);
        Permission.RequestUserPermissions(pending.ToArray(), callbacks);
#else
        Debug.LogWarning($"[Fieldmate] Permissions not granted outside Android: {string.Join(", ", pending)}");
#endif
    }
}
