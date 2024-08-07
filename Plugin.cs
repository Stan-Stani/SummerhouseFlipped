using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using UnityEngine;
using System.Diagnostics;
using static BuildingBlock;
using System.Collections.Generic;
using UnityEngine.TextCore.Text;


namespace SummerhouseFlipped


{

    public static class Log
    {
        internal static ManualLogSource Logger { get; set; }

        public static void Info(string message) => Logger.LogInfo(message);
        public static void Warn(string message) => Logger.LogWarning(message);
        public static void Error(string message) => Logger.LogError(message);
        public static void Debug(string message) => Logger.LogDebug(message);
    }

    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {

        private Harmony harmony;

        internal static ManualLogSource PluginLogger;

        // Global state, cuz I'm dumb and lazy
        internal static bool isBlockPlacerFlippedY = false;
        internal static int yFlipCount = 0;

        private void Awake()
        {
            Log.Logger = Logger;
            Log.Info($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }

    // Separate class for BuildingBlockPicker patches
    class BuildingBlockPickerPatch
    {
        [HarmonyPatch(typeof(BuildingBlockPicker), "Awake")]
        public static class AwakePatch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                Log.Info($"BuildingBlockPicker awakening");
            }
        }

        [HarmonyPatch]
        public static class GetNextBuildingBlockPatch
        {
            static MethodBase TargetMethod()
            {
                // Get all methods named "GetNextBuildingBlock"
                var methods = typeof(BuildingBlockPicker).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name == "GetNextBuildingBlock");

                // You might need to adjust this logic based on the actual method signature
                return methods.FirstOrDefault(m => m.GetParameters().Length == 0);
            }

            [HarmonyPrefix]
            public static bool Prefix(ref object __result)
            {
                Log.Info($"BuildingBlockPickerYOOO");
                // If you want to completely override the original method:
                // __result = YourCustomImplementation();
                // return false;




                Main.CameraController.minMaxPosX = new Vector2(-2000f, 2000f);






                //FieldInfo paddingField = AccessTools.Field(typeof(BuildingBlockGridGenerator), "padding");
                //Log.Info(paddingField.GetValue(__instance));
                // If you want to allow the original method to run:
                return true;
            }


        }
    }

    class MainPatch
    {
        [HarmonyPatch(typeof(Main), "Awake")]
        public static class MainAwakePatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                //Main.GridVisualizer.showGrid = true;
            }
        }
    }

    class GridManagerPatch
    {
        [HarmonyPatch(typeof(GridManager), "Awake")]
        public static class AwakePatch
        {

            public static bool Prefix(GridManager __instance)
            {
                //__instance.debugPrints = true;
                //__instance.depthRange = new Vector2(0f, 3000f);
                //__instance.depthIncrements = 5000f;
                //__instance.gridPositionZ = 20f;
                //Log.Info("set gridposition z");



                return true;
            }

            public static void Postfix(GridManager __instance)
            {
                //__instance.debugPrints = true;
                //__instance.depthRange = new Vector2(0f, 3000f);
                //__instance.depthIncrements = 5000f;
                //__instance.gridPositionZ = 20f;
                //Log.Info("set gridposition z");



            }
        }

        [HarmonyPatch(typeof(GridManager), "SnapPositionToGrid")]
        public static class SnapPositionToGridPatch
        {
            public static void Prefix(Vector3 _originalPosition, int _gridSubdivisions)
            {
                StackTrace stackTrace = new StackTrace(true);
                string callStack = stackTrace.ToString();


                //Log.Info($"Original Position: {_originalPosition} gridSubDivisions {_gridSubdivisions}");
                //UnityEngine.Debug.Log($"TargetMethod was called. Call stack:\n{callStack}");


            }
        }
    }

    class BuildingBlockPatcher()
    {
        [HarmonyPatch(typeof(BuildingBlock), "Awake")]
        public static class AwakePatcher
        {
            //public static bool Prefix(BuildingBlock __instance)
            //{
            //    __instance.zOffset = 75f;
            //    Log.Info("patched block zOffset");
            //    return true;
            //}


        }

        [HarmonyPatch(typeof(BuildingBlock), "GetZPosition")]
        public static class GetZPositionPatcher
        {

            // Nearly identical reimplementation of the original method but increases the number
            // of z positions by 10 times. From 10 to 100 (not counting the 0 position.)
            public static bool Prefix(ref int currentDepthIndex, BuildingBlock __instance, ref float __result)
            {
                List<float> list = new List<float>();
                float item = Main.GridManager.GridPositionZ;
                if (__instance.raycastForZDepth)
                {
                    foreach (RaycastHit depthRaycastHit in __instance.GetDepthRaycastHits(__instance.Flipped))
                    {
                        list.Add(depthRaycastHit.point.z);
                    }

                    if (list.Count == 0)
                    {
                        list.Add(item);
                    }

                    list.Sort();
                    list = list.Distinct().ToList();
                    item = __instance.depthSampleMode switch
                    {
                        DepthSampleMode.Random => list[UnityEngine.Random.Range(0, list.Count)],
                        DepthSampleMode.Greatest => list[list.Count - 1],
                        DepthSampleMode.Lowest => list[0],
                        _ => Main.GridManager.GridPositionZ,
                    };
                }

                if (!__instance.raycastForZDepth)
                {
                    list.Add(item);
                }

                float num = 0.4f;
                // I think this loop only runs if raycastForZDepth is true
                // because otherwise the list.Count - 1 will be 0
                for (int i = 0; i < list.Count - 1; i++)
                {
                    float num2 = list[i];
                    float num3 = list[i + 1] - num2;
                    if (num3 > num)
                    {
                        int num4 = Mathf.FloorToInt(num3 / num);
                        float num5 = num3 / (float)(num4 + 1);
                        for (int j = 1; j <= num4; j++)
                        {
                            list.Insert(i + j, num2 + num5 * (float)j);
                        }

                        i += num4;
                    }
                }

                for (int k = 0; k < 50 + __instance.additionalDepthSteps; k++)
                {
                    list.Add(list.Max() + num);
                }

                for (int l = 0; l < 50 + __instance.additionalDepthSteps; l++)
                {
                    list.Insert(0, list.Min() - num);
                }

                list = __instance.RemoveCloseEntries(list, 0.02f);
                int num6 = list.IndexOf(item);
                int index = Mathf.Clamp(num6 + currentDepthIndex, 0, list.Count - 1);
                currentDepthIndex = Mathf.Clamp(currentDepthIndex, -num6, list.Count - 1 - num6);
                __result = list[index] + __instance.zOffset;

                return false;
            }
        }

        [HarmonyPatch(typeof(BuildingBlock), "CheckFlipping")]
        public static class CheckFlippingPatcher
        {
            public static void Postfix(BuildingBlock __instance)
            {
                if (Plugin.isBlockPlacerFlippedY)
                {
                    FlipY(__instance);
                }
                else
                {
                    UnFlipY(__instance);
                }
            }

            public static void FlipY(BuildingBlock __instance)
            {
                Vector3 flipParentLocalScale = __instance.flipParent.localScale;
                __instance.flipParent.localScale = new Vector3(flipParentLocalScale.x, -1f, flipParentLocalScale.z);
            }

            public static void UnFlipY(BuildingBlock __instance)
            {
                Vector3 flipParentLocalScale = __instance.flipParent.localScale;
                __instance.flipParent.localScale = new Vector3(flipParentLocalScale.x, 1f, flipParentLocalScale.z);
            }


        }
    }


    class BuildingBlockPlacerPatcher
    {



        [HarmonyPatch(typeof(BuildingBlockPlacer), "ToggleForceFlip")]
        public static class ToggleForceFlipPatch
        {

            [HarmonyPostfix]
            public static void Postfix()
            {
                ++Plugin.yFlipCount;
                // Toggle on 3rd click, then every 2
                if (Plugin.yFlipCount == 3)
                {
                    Plugin.isBlockPlacerFlippedY = !Plugin.isBlockPlacerFlippedY;
                    Log.Info("isBlockPlacerFlippedY?:" + Plugin.isBlockPlacerFlippedY);
                    Plugin.yFlipCount = 1;
                }


            }
        }


    }




    // Separate class for SaveLoadConfirmDialogue patches
    class SaveLoadConfirmDialoguePatch
    {
        [HarmonyPatch(typeof(SaveLoadConfirmDialogue), "OpenConfirmDialogue")]
        public static class OpenConfirmDialoguePatch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                Log.Info($"dialogue opening");
                return true;
            }
        }
    }

    class CameraBoundariesPatch
    {

        [HarmonyPatch(typeof(CameraBoundaries), "SetBoundaries")]
        public static class SetBoundariesPatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                Main.CameraController.minMaxPosX = new Vector2(-2000f, 2000f);
                Log.Info($"set expanded camera range");

            }
        }
    }

    ////[HarmonyPatch(typeof(BuildingBlock))]
    ////public class BuildingBlockPatcher
    ////{
    ////    [HarmonyPatch("GetZPosition")]
    ////    [HarmonyPostfix]
    ////    public static void GetZPositionPostfix(BuildingBlock __instance, ref int currentDepthIndex, ref float __result)
    ////    {
    ////        try
    ////        {
    ////            float originalResult = __result;
    ////            float zOffset = Traverse.Create(__instance).Field("zOffset").GetValue<float>();

    ////            // Apply depth change
    ////            float depthStep = 0.4f;
    ////            float depthChange = currentDepthIndex * depthStep;

    ////            // Always apply the depth change
    ////            __result = Main.GridManager.GridPositionZ + depthChange + zOffset;

    ////            Log.Info($"Depth calculated: original={originalResult}, new={__result}, index={currentDepthIndex}, change={depthChange}");
    ////        }
    ////        catch (System.Exception e)
    ////        {
    ////            Log.Error($"Error in GetZPositionPostfix: {e.Message}\n{e.StackTrace}");
    ////        }
    ////    }
    ////}

    //[HarmonyPatch(typeof(BuildingBlockPlacer))]
    //public class BuildingBlockPlacerPatcher
    //{
    //    //[HarmonyPatch("OnManualDepthSelectPositiveDown")]
    //    //[HarmonyPrefix]
    //    //public static bool OnManualDepthSelectPositiveDownPrefix(BuildingBlockPlacer __instance)
    //    //{
    //    //    int currentDepth = Traverse.Create(__instance).Field("currentDepthIndex").GetValue<int>();
    //    //    currentDepth++;
    //    //    Traverse.Create(__instance).Field("currentDepthIndex").SetValue(currentDepth);
    //    //    Log.Info($"Depth increased to {currentDepth}");
    //    //    return false; // Skip the original method
    //    //}

    //    //[HarmonyPatch("OnManualDepthSelectNegativeDown")]
    //    //[HarmonyPrefix]
    //    //public static bool OnManualDepthSelectNegativeDownPrefix(BuildingBlockPlacer __instance)
    //    //{
    //    //    int currentDepth = Traverse.Create(__instance).Field("currentDepthIndex").GetValue<int>();
    //    //    currentDepth--;
    //    //    Traverse.Create(__instance).Field("currentDepthIndex").SetValue(currentDepth);
    //    //    Log.Info($"Depth decreased to {currentDepth}");
    //    //    return false; // Skip the original method
    //    //}

    //    //[HarmonyPatch("PreviewPosition")]
    //    //[HarmonyPostfix]
    //    //public static void PreviewPositionPostfix(BuildingBlockPlacer __instance, BuildingBlock _previewBlock, ref Vector3 __result)
    //    //{
    //    //    try
    //    //    {
    //    //        int currentDepthIndex = Traverse.Create(__instance).Field("currentDepthIndex").GetValue<int>();
    //    //        float zPos = _previewBlock.GetZPosition(ref currentDepthIndex);
    //    //        __result.z = zPos;
    //    //        Log.Info($"Preview position updated: z={zPos}, depth index={currentDepthIndex}");
    //    //    }
    //    //    catch (System.Exception e)
    //    //    {
    //    //        Log.Error($"Error in PreviewPositionPostfix: {e.Message}\n{e.StackTrace}");
    //    //    }
    //    //}
    //}


    //[HarmonyPatch(typeof(InputManager))]
    //public class InputManagerPatcher
    //{
    //    //[HarmonyPatch("OnManualDepthSelectPositiveDown")]
    //    //[HarmonyPrefix]
    //    //public static bool OnManualDepthSelectPositiveDownPrefix()
    //    //{
    //    //    ModifyDepth(1);
    //    //    return false; // Skip the original method
    //    //}

    //    //[HarmonyPatch("OnManualDepthSelectNegativeDown")]
    //    //[HarmonyPrefix]
    //    //public static bool OnManualDepthSelectNegativeDownPrefix()
    //    //{
    //    //    ModifyDepth(-1);
    //    //    return false; // Skip the original method
    //    //}

    //    //private static void ModifyDepth(int change)
    //    //{
    //    //    try
    //    //    {
    //    //        BuildingBlockPlacer placer = UnityEngine.Object.FindObjectOfType<BuildingBlockPlacer>();
    //    //        if (placer != null)
    //    //        {
    //    //            int currentDepth = Traverse.Create(placer).Field("currentDepthIndex").GetValue<int>();
    //    //            currentDepth += change;
    //    //            Traverse.Create(placer).Field("currentDepthIndex").SetValue(currentDepth);
    //    //            Log.Info($"Depth changed to {currentDepth}");

    //    //            // Force update of preview block position
    //    //            Traverse.Create(placer).Method("SpawnNextBlockPreview", new object[] { true }).GetValue();
    //    //        }
    //    //    }
    //    //    catch (System.Exception e)
    //    //    {
    //    //        Log.Error($"Error in ModifyDepth: {e.Message}\n{e.StackTrace}");
    //    //    }
    //    //}
    //}

    //[HarmonyPatch]
    //public class BuildingBlockGridGeneratorPatch
    //{
    //    [HarmonyPatch(typeof(BuildingBlockGridGenerator))]
    //    [HarmonyPatch(MethodType.Constructor)]
    //    public static bool Prefix(BuildingBlockGridGenerator __instance)
    //    {
    //        // Use reflection to set the padding field
    //        FieldInfo paddingField = AccessTools.Field(typeof(BuildingBlockGridGenerator), "padding");
    //        if (paddingField != null)
    //        {
    //            Log.Info("setting padding");

    //           paddingField.SetValue(__instance, 5000f); // Set to your desired value
    //            Log.Info(paddingField.GetValue(__instance));
    //        }
    //        else
    //        {
    //            Log.Error("Could not find padding field in BuildingBlockGridGenerator");
    //        }

    //        return true;
    //    }
    //}


}

