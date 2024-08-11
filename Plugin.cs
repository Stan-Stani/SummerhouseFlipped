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


        SaveData SaveData;

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

    public static class SavedBuildingBlockPatcher
    {

        /// <summary>
        /// Id, isFlippedY
        /// </summary>
        public static Dictionary<int, bool> PatchedSavedBlockState = new();
        public static bool flippedY = false;

        // Gotta dynamically look at transofform vector of block to determine if it's y flipped
        // then save it to the dictionary and use tht for serialization on save
        [HarmonyPatch(typeof(SavedBuildingBlock), MethodType.Constructor)]
        public static void Postfix(BuildingBlock block, SavedBuildingBlock __instance)
        {

            PatchedSavedBlockState[block.saveID] = block.flipParent.localScale.y == -1f;

        }
    }

    public static class SaveGameProcess
    {

        [Serializable]
        public class SavedBuildingBlockExtended : SavedBuildingBlock
        {

            public bool flippedY = false;

            public SavedBuildingBlockExtended(BuildingBlock block) : base(block)
            {
                flippedY = block.flipParent.localScale.y == -1f;
            }


        }


        [Serializable]
        public class SaveDataExtended : SaveData
        {

            public new List<SavedBuildingBlockExtended> buildingBlocks = new List<SavedBuildingBlockExtended>();
            public SaveDataExtended()
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
        }

        [HarmonyPatch(typeof(SaveGameManager))]
        public static class SaveGameManagerPatcher
        {

            // Thanks Claude Sonnet 3.5!
            public class SpecificPropertiesContractResolver : DefaultContractResolver
            {
                private readonly Dictionary<Type, HashSet<string>> _includeProperties;

                public SpecificPropertiesContractResolver()
                {
                    _includeProperties = new Dictionary<Type, HashSet<string>>();
                }

                public void IncludeProperties(Type type, params string[] propertyNames)
                {
                    if (!_includeProperties.ContainsKey(type))
                        _includeProperties[type] = new HashSet<string>();

                    foreach (var name in propertyNames)
                        _includeProperties[type].Add(name);
                }

                protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
                {
                    var allProperties = base.CreateProperties(type, memberSerialization);

                    if (_includeProperties.TryGetValue(type, out HashSet<string> includeProperties))
                    {
                        return allProperties.Where(p => includeProperties.Contains(p.PropertyName)).ToList();
                    }

                    return allProperties;
                }
            }

            [HarmonyPatch("SaveGame")]
            public static bool Prefix(SaveGameManager __instance, int slotNumber, bool _isAutoSave = false)
            {
                Log.Info("lalonde " + "hello");




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







                Log.Info("Bish");

                var resolver = new SpecificPropertiesContractResolver();
                resolver.IncludeProperties(typeof(UnityEngine.Vector3), "x", "y", "z");


                JsonSerializerSettings settings = new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    ContractResolver = resolver
                };

                string contents = JsonConvert.SerializeObject(new SaveDataExtended(), settings);
                Log.Info("CONTENTS" + contents);

                File.WriteAllText(saveFilePath, contents);
                Log.Info("lalonde " + contents);
                if (__instance.debugPrints)
                {
                    UnityEngine.Debug.Log("Game Saved to slot " + slotNumber);
                }
                __instance.OnGameSave.Invoke();
                if (!_isAutoSave)
                {
                    Main.UIPopUpManager.PopUp(__instance.gameSavedPopup);
                }

                return false;
            }

            [HarmonyPatch("LoadGame")]
            public static bool LoadGame(SaveGameManager __instance, int slotNumber)
            {
                Main.AudioManager.PlayGameLoaded();
                string saveFilePath = __instance.GetSaveFilePath(slotNumber);
                if (!Directory.Exists(Path.GetDirectoryName(saveFilePath)))
                {
                    return false;
                }

                SaveDataExtended saveData = ReadSaveDataFromDiskExtended(saveFilePath);
                if (saveData != null)
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
                    return JsonConvert.DeserializeObject<SaveDataExtended>(File.ReadAllText(_saveFilePath));
                }

                UnityEngine.Debug.LogError("No save file found!");
                return null;
            }


            public static IEnumerator ApplySaveDataExtended(SaveData _saveData, SaveGameManager saveGameManagerInstance)
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

                foreach (SavedBuildingBlockExtended buildingBlock in _saveData.buildingBlocks)
                {
                    Main.BuildingBlockPlacer.RecreateSavedBlock(buildingBlock);
                    yield return null; // Yield after each block to prevent freezing
                }

                Main.UIPopUpManager.PopUp(saveGameManagerInstance.gameLoadedPopup);
                Main.UndoManager.ClearUndoHistory();
                Main.UndoManager.RegisterUndoStep();

                yield break;
            }

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

        //[HarmonyPatch]
        //public static class GetNextBuildingBlockPatch
        //{


        //    [HarmonyPrefix]
        //    public static bool Prefix(ref object __result)
        //    {
        //        Log.Info($"BuildingBlockPickerYOOO");
        //        // If you want to completely override the original method:
        //        // __result = YourCustomImplementation();
        //        // return false;




        //        Main.CameraController.minMaxPosX = new Vector2(-2000f, 2000f);






        //        //FieldInfo paddingField = AccessTools.Field(typeof(BuildingBlockGridGenerator), "padding");
        //        //Log.Info(paddingField.GetValue(__instance));
        //        // If you want to allow the original method to run:
        //        return true;
        //    }


        //}
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

 






    [HarmonyPatch(typeof(BuildingBlockPlacer))]
    public static class BuildingBlockPlacerPatcher
    {
        //[HarmonyPatch("GetNextBlock")]
        //public static bool Prefix(bool _repeatLastBlock, BuildingBlockPlacer __instance, ref BuildingBlock __result)
        //{
        //    BuildingBlock buildingBlock = _repeatLastBlock ? Main.BuildingBlockPicker.GetLastPickedBlockAgain() : Main.BuildingBlockPicker.GetNextBuildingBlock();
        //    if (__instance.debugPrints)
        //    {
        //        UnityEngine.Debug.Log("Picked next Building Block: " + buildingBlock.gameObject.name);
        //    }

        //    __result = buildingBlock;
        //    Log.Info("lalonde" + buildingBlock.GetType().ToString());



        //    return false; // Skip original method
        //}





        [HarmonyPatch("ToggleForceFlip")]
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




        [HarmonyPatch("RecreateSavedBlock")]
        public static bool Prefix(SaveGameProcess.SavedBuildingBlockExtended _block, BuildingBlockPlacer __instance)
        {
            BuildingBlock blockByID = Main.SaveGameManager.buildingBlockLibrary.GetBlockByID(_block.blockID);
            if (blockByID != null)
            {
                BuildingBlock buildingBlock = UnityEngine.Object.Instantiate(blockByID);

                buildingBlock.transform.parent = __instance.transform;
                buildingBlock.transform.position = _block.position;
                __instance.allPlacedBlocks.Add(buildingBlock);
                if (_block.flipped)
                {
                    buildingBlock.Flip();
                }
                if (_block.flippedY)
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


}


