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
using static UnityEngine.ScriptingUtility;
using System.IO;
using System.Runtime.CompilerServices;
using System;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections;
using static UnityEngine.UIElements.UIR.BestFitAllocator;
using static BuildingBlockPicker;
using UnityEngine.Rendering.VirtualTexturing;
using UnityEngine.UIElements;


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
        internal static int yFlipCount = 1;

        private void Awake()
        {
            Log.Logger = Logger;

            harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }

    public static class SaveGameProcess
    {

        [Serializable]
        public class SavedBuildingBlockExtended
        {

            public bool FlippedY = false;


            // These other fields are just a reimplementation of the original SavedBuildingBlock
            public Vector3 position;

            public bool flipped;

            public int blockID;

            // Parameterless constructor for deserialization
            public SavedBuildingBlockExtended()
            {
            }

            public SavedBuildingBlockExtended(BuildingBlock block)
            {



                position = block.transform.position;
                flipped = block.Flipped;
                blockID = block.saveID;

                try
                {
                    // Don't understand why there're is this 2 child deep chain I have to check
                    // I don't know Unity well enough but Anthropic's Claude says that
                    // prefabs can have more information that gets setup behind the scenes and that that's
                    // possibly where this child and grandchild come from, because I don't see them
                    // added in the vanilla code anywhere... Only reason I know they're there is
                    // Unity Explorer, thank goodness for it.
                    var transformThatControlsFlip = block.pivotParent.GetChild(0).GetChild(0);

                    // We can tell it's Y flipped literally just from looking at this Transform
                    FlippedY = transformThatControlsFlip.localScale.y == -1f;
                }
                catch (Exception exception)
                {
                    Log.Error(exception.Message);
                }
            }




        }


        [Serializable]
        public class SaveDataExtended : SaveData
        {

            public new List<SavedBuildingBlockExtended> buildingBlocks = new List<SavedBuildingBlockExtended>();
            /// <summary>
            /// 
            /// </summary>
            /// <param name="willInitializeFromCurrentGameState">So that when JSON serializer uses this constructor
            /// it gets an effectively empty constructor.</param>
            public SaveDataExtended(bool willInitializeFromCurrentGameState = false)
            {
                if (willInitializeFromCurrentGameState)
                {
                    try
                    {
                        foreach (BuildingBlock allPlacedBlock in Main.BuildingBlockPlacer.AllPlacedBlocks)
                        {
                            buildingBlocks.Add(new SavedBuildingBlockExtended(allPlacedBlock));
                        }

                        saveGameVersion = Main.SaveGameManager.SaveGameVersion;
                        mapName = Main.SceneLoadManager.CurrentSceneName;
                        cameraPos = Main.CameraController.transform.position;
                        if (Main.ColorManager != null)
                        {
                            savedPalette = Main.ColorManager.activePalette;
                        }
                    }
                    catch (Exception exception)
                    {
                        Log.Error(exception.Message);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(SaveGameManager))]
        public static class SaveGameManagerPatcher
        {

            public static SavedBuildingBlockExtended CurrentlyRecreatingSavedBuildingBlockExtended = null;

            [HarmonyPatch("SaveGame")]
            public static bool Prefix(SaveGameManager __instance, int slotNumber, bool _isAutoSave = false)
            {
                if (!_isAutoSave)
                {
                    Main.AudioManager.PlayGameSaved();
                }
                string saveFilePath = __instance.GetSaveFilePath(slotNumber);
                string directoryName = Path.GetDirectoryName(saveFilePath);
                if (!Directory.Exists(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }


                string contents = "";
                try
                {
                    contents = JSON.Serialize(new SaveDataExtended(true));

                    File.WriteAllText(saveFilePath, contents);
                    if (__instance.debugPrints)
                    {
                        UnityEngine.Debug.Log("Game Saved to slot " + slotNumber);
                    }
                    __instance.OnGameSave.Invoke();
                    if (!_isAutoSave)
                    {
                        Main.UIPopUpManager.PopUp(__instance.gameSavedPopup);
                    }

                }
                catch (Exception e)
                {
                    Log.Error(e.Message);
                    File.WriteAllText(saveFilePath + "helloError", e.Message);
                }


                return false;
            }

            [HarmonyPatch("LoadGame")]
            public static bool Prefix(SaveGameManager __instance, int slotNumber)
            {
                Main.AudioManager.PlayGameLoaded();
                string saveFilePath = __instance.GetSaveFilePath(slotNumber);
                if (!Directory.Exists(Path.GetDirectoryName(saveFilePath)))
                {
                    return false;
                }
                SaveDataExtended saveData = ReadSaveDataFromDiskExtended(saveFilePath);
                Log.Info($"saveData block length: ${saveData.buildingBlocks.Count} blocks.");
                {
                    if (saveData.saveGameVersion != __instance.saveGameVersion)
                    {
                        return false;
                    }

                    Main.UIManager.FadeToBlackAndExecute(delegate
                    {
                        __instance.StartCoroutine(ApplySaveDataExtended(saveData, __instance));
                    });
                }

                if (__instance.debugPrints)
                {
                    UnityEngine.Debug.Log("Game Loaded from slot " + slotNumber);
                }

                return false;
            }

            public static SaveDataExtended ReadSaveDataFromDiskExtended(string _saveFilePath)
            {
                if (File.Exists(_saveFilePath))
                {
                    string fileJSON = (File.ReadAllText(_saveFilePath));
                    Log.Info($"Save file text: ${fileJSON}");
                    try
                    {
                        var deserializedSaveData = JSON.Deserialize<SaveDataExtended>(fileJSON);
                        return deserializedSaveData;
                    }
                    catch (Exception exception)
                    {

                        Log.Error(exception.Message);
                        Log.Error(exception.StackTrace);
                        Log.Error($"fileJson: {fileJSON}");
                    }
                }

                UnityEngine.Debug.LogError("No save file found!");
                return null;
            }


            public static IEnumerator ApplySaveDataExtended(SaveDataExtended _saveData, SaveGameManager saveGameManagerInstance)
            {
                Main.SceneLoadManager.LoadScene(_saveData.mapName);
                while (Main.SceneLoadManager.IsLoadingScene)
                {
                    yield return null;
                }

                Main.ColorManager.ApplySavePalette(_saveData.savedPalette);
                Main.CameraController.TeleportToPosition(_saveData.cameraPos);
                Main.UndoManager.ClearUndoHistory();
                Main.UndoManager.RegisterUndoStep();
                Main.BuildingBlockPlacer.ClearAllBlocks();

                var dummyBuildingBlock = UnityEngine.Object.Instantiate(Main.SaveGameManager.buildingBlockLibrary.GetBlockByID(1));

                var initializedDummyBuildingBlock = dummyBuildingBlock;

                SavedBuildingBlock dummySavedBuildingBlock = new(initializedDummyBuildingBlock);
                Log.Info($"Count of buikldingBLocks in savedata = {_saveData.buildingBlocks.Count()}");
                foreach (SavedBuildingBlockExtended savedBuildingBlockExtended in _saveData.buildingBlocks)
                {

                    CurrentlyRecreatingSavedBuildingBlockExtended = savedBuildingBlockExtended;
                    Main.BuildingBlockPlacer.RecreateSavedBlock(dummySavedBuildingBlock);
                }
                UnityEngine.Object.Destroy(dummyBuildingBlock);

                Main.UIPopUpManager.PopUp(saveGameManagerInstance.gameLoadedPopup);
                Main.UndoManager.ClearUndoHistory();
                Main.UndoManager.RegisterUndoStep();

                yield break;
            }

        }




    }
    
    // Not used yet
    class BuildingBlockPickerPatch
    {
        [HarmonyPatch(typeof(BuildingBlockPicker), "Awake")]
        public static class AwakePatch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {

            }
        }


    }

    // Not used yet
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

    // For enabling the debug grid, just for fun.
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
                //


                return true;
            }

            public static void Postfix(GridManager __instance)
            {
                //__instance.debugPrints = true;
                //__instance.depthRange = new Vector2(0f, 3000f);
                //__instance.depthIncrements = 5000f;
                //__instance.gridPositionZ = 20f;
                //


            }
        }

        [HarmonyPatch(typeof(GridManager), "SnapPositionToGrid")]
        public static class SnapPositionToGridPatch
        {
            public static void Prefix(Vector3 _originalPosition, int _gridSubdivisions)
            {
                StackTrace stackTrace = new StackTrace(true);
                string callStack = stackTrace.ToString();


                //                //UnityEngine.Debug.Log($"TargetMethod was called. Call stack:\n{callStack}");


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
            //                //    return true;
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








    [HarmonyPatch(typeof(BuildingBlockPlacer))]
    public static class BuildingBlockPlacerPatcher
    {


        [HarmonyPatch("ToggleForceFlip")]
        public static void Postfix()
        {
            ++Plugin.yFlipCount;
            // Toggle every 2 clicks
            if (Plugin.yFlipCount == 3)
            {
                Plugin.isBlockPlacerFlippedY = !Plugin.isBlockPlacerFlippedY;
                Plugin.yFlipCount = 1;
            }


        }




        [HarmonyPatch("RecreateSavedBlock")]
        public static bool Prefix(SavedBuildingBlock _block, BuildingBlockPlacer __instance)
        {
            var _dummyBlock = _block;
            SaveGameProcess.SavedBuildingBlockExtended savedBlockExtended = SaveGameProcess.SaveGameManagerPatcher.CurrentlyRecreatingSavedBuildingBlockExtended;
            BuildingBlock blockByID = Main.SaveGameManager.buildingBlockLibrary.GetBlockByID(savedBlockExtended.blockID);
            if (blockByID != null)
            {
                BuildingBlock buildingBlock = UnityEngine.Object.Instantiate(blockByID);

                buildingBlock.transform.parent = __instance.transform;
                buildingBlock.transform.position = savedBlockExtended.position;
                __instance.allPlacedBlocks.Add(buildingBlock);
                if (savedBlockExtended.flipped)
                {
                    buildingBlock.Flip();
                }
                if (savedBlockExtended.FlippedY)
                {
                    BuildingBlockPatcher.CheckFlippingPatcher.FlipY(buildingBlock);

                }


            }
            return false;
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
                return true;
            }
        }
    }

    class CameraBoundariesPatch
    {
        /// <summary>
        /// Let the camera move (and thus blocks be placed) much further in the left and right directions. 
        /// </summary>
        [HarmonyPatch(typeof(CameraBoundaries), "SetBoundaries")]
        public static class SetBoundariesPatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                Main.CameraController.minMaxPosX = new Vector2(-2000f, 2000f);

            }
        }
    }


}


