using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Fieldmate.XR
{
    /// <summary>
    /// Loads the controller models the runtime itself renders (XR_FB_render_model, #80): the glTF (GLB) of the
    /// controllers in use, the same models Meta's home shows, fetched at runtime so nothing Meta-owned is committed. Call
    /// <see cref="TryLoadController"/> once the session runs; <see cref="ControllerModel"/> does and builds the mesh.
    /// Block-scoped namespace: OpenXR features are ScriptableObjects.
    /// </summary>
#if UNITY_EDITOR
    [UnityEditor.XR.OpenXR.Features.OpenXRFeature(UiName = "Fieldmate: Controller Render Models",
        BuildTargetGroups = new[] { UnityEditor.BuildTargetGroup.Android },
        Company = "Fieldmate",
        Desc = "Loads the runtime's controller models (XR_FB_render_model) for the presence visuals.",
        OpenxrExtensionStrings = ExtensionString,
        Version = "0.1.0",
        FeatureId = FeatureId)]
#endif
    public sealed class RenderModelFeature : OpenXRFeature
    {
        public const string FeatureId = "com.fieldmate.openxr.rendermodel";
        public const string ExtensionString = "XR_FB_render_model";

        public const int TypePathInfo = 1000119000;
        public const int TypeProperties = 1000119001;
        public const int TypeBuffer = 1000119002;
        public const int TypeLoadInfo = 1000119003;
        public const int TypeCapabilitiesRequest = 1000119005;

        /// <summary>glTF subsets the app can read: both (subset 2 is Quest Pro / Touch Plus models).</summary>
        public const ulong SupportsGltfSubsets = 0x1 | 0x2;

        [StructLayout(LayoutKind.Sequential)]
        public struct PathInfo
        {
            public int type;
            public IntPtr next;
            public ulong path;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CapabilitiesRequest
        {
            public int type;
            public IntPtr next;
            public ulong flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Properties
        {
            public int type;
            public IntPtr next;
            public uint vendorId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] modelName;
            public ulong modelKey;
            public uint modelVersion;
            public ulong flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LoadInfo
        {
            public int type;
            public IntPtr next;
            public ulong modelKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Buffer
        {
            public int type;
            public IntPtr next;
            public uint bufferCapacityInput;
            public uint bufferCountOutput;
            public IntPtr buffer;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetInstanceProcAddr(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr function);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int EnumeratePaths(ulong session, uint capacity, out uint count, [In, Out] PathInfo[] paths);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetProperties(ulong session, ulong path, ref Properties properties);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int LoadModel(ulong session, ref LoadInfo info, ref Buffer buffer);

        private static EnumeratePaths enumeratePaths;
        private static GetProperties getProperties;
        private static LoadModel loadModel;
        private static ulong session;
        private static bool pathsEnumerated;

        /// <summary>True once the runtime offered the extension and a session exists.</summary>
        public static bool IsReady => session != 0 && loadModel != null;

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            if (!OpenXRRuntime.IsExtensionEnabled(ExtensionString))
            {
                Debug.LogWarning("[Presence] XR_FB_render_model not available: no runtime controller models");
                return true; // optional: the app runs without the models
            }

            var getProc = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddr>(xrGetInstanceProcAddr);
            enumeratePaths = Load<EnumeratePaths>(getProc, xrInstance, "xrEnumerateRenderModelPathsFB");
            getProperties = Load<GetProperties>(getProc, xrInstance, "xrGetRenderModelPropertiesFB");
            loadModel = Load<LoadModel>(getProc, xrInstance, "xrLoadRenderModelFB");
            return true;
        }

        protected override void OnSessionCreate(ulong xrSession)
        {
            session = xrSession;
            pathsEnumerated = false;
        }

        protected override void OnSessionDestroy(ulong xrSession) => session = 0;

        protected override void OnInstanceDestroy(ulong xrInstance)
        {
            enumeratePaths = null;
            getProperties = null;
            loadModel = null;
        }

        private static T Load<T>(GetInstanceProcAddr getProc, ulong instance, string name) where T : Delegate =>
            getProc(instance, name, out var pointer) == 0 && pointer != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(pointer) : null;

        /// <summary>The GLB of the left or right controller model, and its runtime name; false until it is available.</summary>
        public static bool TryLoadController(bool left, out byte[] glb, out string modelName)
        {
            glb = null;
            modelName = null;
            if (!IsReady || enumeratePaths == null || getProperties == null)
            {
                return false;
            }

            if (!pathsEnumerated)
            {
                // The runtime expects the paths to be enumerated before properties are queried.
                if (enumeratePaths(session, 0, out var count, null) != 0)
                {
                    return false;
                }

                var paths = new PathInfo[count];
                for (var i = 0; i < paths.Length; i++)
                {
                    paths[i].type = TypePathInfo;
                }

                if (enumeratePaths(session, count, out _, paths) != 0)
                {
                    return false;
                }

                pathsEnumerated = true;
            }

            var request = new CapabilitiesRequest { type = TypeCapabilitiesRequest, flags = SupportsGltfSubsets };
            var requestPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CapabilitiesRequest>());
            try
            {
                Marshal.StructureToPtr(request, requestPtr, false);
                var properties = new Properties { type = TypeProperties, next = requestPtr, modelName = new byte[64] };
                var path = StringToPath(left ? "/model_fb/controller/left" : "/model_fb/controller/right");
                var result = getProperties(session, path, ref properties);
                if (result != 0 || properties.modelKey == 0)
                {
                    return false; // XR_RENDER_MODEL_UNAVAILABLE_FB until the controller is connected
                }

                modelName = System.Text.Encoding.UTF8.GetString(properties.modelName).TrimEnd('\0');
                var info = new LoadInfo { type = TypeLoadInfo, modelKey = properties.modelKey };
                var buffer = new Buffer { type = TypeBuffer };
                if (loadModel(session, ref info, ref buffer) != 0 || buffer.bufferCountOutput == 0)
                {
                    return false;
                }

                var bytes = Marshal.AllocHGlobal((int)buffer.bufferCountOutput);
                try
                {
                    buffer.bufferCapacityInput = buffer.bufferCountOutput;
                    buffer.buffer = bytes;
                    if (loadModel(session, ref info, ref buffer) != 0)
                    {
                        return false;
                    }

                    glb = new byte[buffer.bufferCountOutput];
                    Marshal.Copy(bytes, glb, 0, glb.Length);
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(bytes);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(requestPtr);
            }
        }
    }
}
