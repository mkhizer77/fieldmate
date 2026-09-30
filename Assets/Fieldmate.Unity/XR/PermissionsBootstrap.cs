using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fieldmate.XR
{
    /// <summary>
    /// Requests every runtime permission the app needs in one go (scene data, microphone). Separate simultaneous
    /// requests can cancel each other on Android. Components ask <see cref="WhenAnswered"/> instead of requesting.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class PermissionsBootstrap : MonoBehaviour
    {
        private static readonly string[] Needed = { QuestPermissions.Scene, QuestPermissions.Microphone };
        private static PermissionsBootstrap instance;

        private readonly Dictionary<string, bool> answers = new();
        private readonly Dictionary<string, List<Action<bool>>> waiting = new();

        private void Awake() => instance = this;

        private void Start() => QuestPermissions.Request(Needed, (permission, granted, _) => Answer(permission, granted));

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>Calls back once the permission is answered (immediately if it already was, or outside Android).</summary>
        public static void WhenAnswered(string permission, Action<bool> callback)
        {
            if (instance == null)
            {
                callback(QuestPermissions.IsGranted(permission));
                return;
            }

            if (instance.answers.TryGetValue(permission, out var granted))
            {
                callback(granted);
                return;
            }

            if (!instance.waiting.TryGetValue(permission, out var list))
            {
                instance.waiting[permission] = list = new List<Action<bool>>();
            }

            list.Add(callback);
        }

        private void Answer(string permission, bool granted)
        {
            answers[permission] = granted;
            Debug.Log($"[Permissions] {permission}: {(granted ? "granted" : "denied")}");
            if (waiting.Remove(permission, out var list))
            {
                foreach (var callback in list)
                {
                    callback(granted);
                }
            }
        }
    }
}
