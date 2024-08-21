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

    public static class JSONSTUFF
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

        public static string Serialize(object obj)
        {
            var resolver = new SpecificPropertiesContractResolver();
            resolver.IncludeProperties(typeof(UnityEngine.Vector3), "x", "y", "z");
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = resolver,
                Error = (sender, args) =>
                {
                    Log.Error($"SERIALIZATION ERROR: {args.ErrorContext.Error.Message}");

                    Log.Error($"SERIALIZATION ERROR CONTINUED: Current Object: {args.CurrentObject}");
                    Log.Error($"SERIALIZATION ERROR CONTINUED: Member: {args.ErrorContext.Member}");
                    Log.Error($"SERIALIZATION ERROR CONTINUED: Original Object: {args.ErrorContext.OriginalObject}");
                    Log.Error($"SERIALIZATION ERROR CONTINUED: Path: ${args.ErrorContext.Path}");
                    //Log.Error($"SERIALIZATION ERROR CONTINUED: Stack: {args.ErrorContext.Error.StackTrace}");

                    //args.ErrorContext.Handled = true;

                }
            };

            return JsonConvert.SerializeObject(obj, settings);
        }

        public static T Deserialize<T>(string str)
        {
            var resolver = new SpecificPropertiesContractResolver();
            resolver.IncludeProperties(typeof(UnityEngine.Vector3), "x", "y", "z");
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = resolver,
                Error = (sender, args) =>
                {
                    Log.Error($"DE-SERIALIZATION ERROR: {args.ErrorContext.Error.Message}");
                    Log.Error($"DE-SERIALIZATION ERROR CONTINUED: Current Object: {args.CurrentObject}");
                    Log.Error($"DE-SERIALIZATION ERROR CONTINUED: Member: {args.ErrorContext.Member}");
                    Log.Error($"DE-SERIALIZATION ERROR CONTINUED: Original Object: {args.ErrorContext.OriginalObject}");
                    Log.Error($"DE-SERIALIZATION ERROR CONTINUED: Path: ${args.ErrorContext.Path}");
                    //Log.Error($"De-SERIALIZATION ERROR CONTINUED: Stack: {args.ErrorContext.Error.StackTrace}");

                    //args.ErrorContext.Handled = true;

                }
            };

            return JsonConvert.DeserializeObject<T>(str, settings);
        }



        static JsonSerializerSettings settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ContractResolver = new SpecificPropertiesContractResolver(),
        };
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
            Log.Info($"{block.flipParent.localScale.y == -1f} yo");
            PatchedSavedBlockState[block.saveID] = block.flipParent.localScale.y == -1f;

        }
    }

    public static class SaveGameProcess
    {

        [Serializable]
        public class SavedBuildingBlockExtended
        {

            public bool FlippedY = false;


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
                    // TODO: see line 534 in BuildingBlock
                    //    BuildingBlock buildingBlock = Object.Instantiate(_block, position, Quaternion.identity);
                    // Somehow the flip info in localScale of flipparent is cleared I think and the block's "flipped" field is used to keep track
                    Log.Info("HEELO STAN");
                    Log.Info($"Flipped?: ${block.Flipped}");
                    Log.Info($"localSCALE vbasetransformpositiont: HEY {JSONSTUFF.Serialize(block.transform.position)}");
                    Log.Info($"localSCALE BASE LOCALSCALEt: HEY {JSONSTUFF.Serialize(block.transform.localScale)}");
                    Log.Info($"localSCALE FLIPPAREnt: HEY {JSONSTUFF.Serialize(block.flipParent.localScale)}");
                    Log.Info($"localSCALE:XXX HEY {JSONSTUFF.Serialize(block.transform.localScale.y)}");
                    Log.Info($"localSCALE: HEY {JSONSTUFF.Serialize(block.transform.rotation)}");
                    FlippedY = block.flipParent.localScale.y == -1f;
                    Log.Info(block.flipParent.localScale.ToString());
                    Log.Info(block.transform.localScale.ToString());
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
            public SaveDataExtended()
            {
                Log.Info("STAN STAn");
                try
                {
                    //var defaultToJSON = JSONSTUFF.GetDefault();
                    Log.Info("saveDataExtended constructor before foreach");
                    Log.Info(Main.BuildingBlockPlacer.AllPlacedBlocks.GetType().ToString());
                    Log.Info((Main.BuildingBlockPlacer.AllPlacedBlocks is null).ToString());
                    foreach (BuildingBlock allPlacedBlock in Main.BuildingBlockPlacer.AllPlacedBlocks)
                    {
                        Log.Info("adding to list");
                        Log.Info($"AllPlacedBlock: ${JSONSTUFF.Serialize(allPlacedBlock.flipParent.localScale)}");
                        Log.Info($"AllPlacedBlock: ${JSONSTUFF.Serialize(allPlacedBlock.transform.localScale)}");
                        Log.Info($"----------------------------------------");
                        buildingBlocks.Add(new SavedBuildingBlockExtended(allPlacedBlock));
                    }

                    Log.Info("saveDataExtended constructor before version");
                    saveGameVersion = Main.SaveGameManager.SaveGameVersion;
                    Log.Info("saveDataExtended constructor before currentscenename");
                    mapName = Main.SceneLoadManager.CurrentSceneName;
                    Log.Info("saveDataExtended constructor before position");
                    cameraPos = Main.CameraController.transform.position;
                    if (Main.ColorManager != null)
                    {
                        savedPalette = Main.ColorManager.activePalette;
                    }
                }
                catch (Exception exception)
                {
                    Log.Info("saveDataExtended constructor exception");
                    Log.Error(exception.Message);
                    Log.Info("saveDataExtended constructor exception");
                }
                Log.Info("saveDataExtended constructor DONE");
            }
        }

        [HarmonyPatch(typeof(SaveGameManager))]
        public static class SaveGameManagerPatcher
        {

            public static SavedBuildingBlockExtended CurrentlyRecreatingSavedBuildingBlockExtended = null;

            [HarmonyPatch("SaveGame")]
            public static bool Prefix(SaveGameManager __instance, int slotNumber, bool _isAutoSave = false)
            {
                Log.Info("SAVE GAME PATCHING IS WORKING");




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


                string contents = "";
                try
                {
                    //var defaultToJSON = JSONSTUFF.GetDefault();
                    contents = JSONSTUFF.Serialize(new SaveDataExtended());
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
                //var defaultToJSON = JSONSTUFF.GetDefault();
                SaveDataExtended saveData = ReadSaveDataFromDiskExtended(saveFilePath);
                //Log.Info($"Save data just after deserialization: {defaultToJSON(saveData)}");
                if (saveData != null)
                {
                    if (saveData.saveGameVersion != __instance.saveGameVersion)
                    {
                        return false;
                    }

                    Log.Info("LOL SCREW YOU STAN");
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
                    try
                    {
                        return JSONSTUFF.Deserialize<SaveDataExtended>(fileJSON);
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
                Log.Info($"{dummyBuildingBlock is null}");

                var initializedDummyBuildingBlock = dummyBuildingBlock;

                SavedBuildingBlock dummySavedBuildingBlock = new(initializedDummyBuildingBlock);

                foreach (SavedBuildingBlockExtended savedBuildingBlockExtended in _saveData.buildingBlocks)
                {
                    //TODO: Probably will just need to reimplement this taking an extended building block
                    Log.Info("STAN WE TRYING TO RECREATESAVEDBLOCKLOL");
                    CurrentlyRecreatingSavedBuildingBlockExtended = savedBuildingBlockExtended;
                    Main.BuildingBlockPlacer.RecreateSavedBlock(dummySavedBuildingBlock);
                    yield return null; // Yield after each block to prevent freezing
                }

                Main.UIPopUpManager.PopUp(saveGameManagerInstance.gameLoadedPopup);
                Main.UndoManager.ClearUndoHistory();
                Main.UndoManager.RegisterUndoStep();

                yield break;
            }

        }




    }

    class UndoManagerPatcher
    {
        [HarmonyPatch(typeof(UndoManager), "RegisterUndoStep")]
        public static bool Prefix(UndoManager __instance)
        {

            {
                if (__instance.currentIndex < __instance.undoHistory.Count - 1)
                {
                    __instance.undoHistory.RemoveRange(__instance.currentIndex + 1, __instance.undoHistory.Count - __instance.currentIndex - 1);
                }

                Log.Info("running undo clearer");
                __instance.undoHistory.Add(new SaveGameProcess.SaveDataExtended());
                __instance.currentIndex = __instance.undoHistory.Count - 1;
            }

            return false;
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
                Log.Info("HELLO AWAKE STAN ");
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

        [HarmonyPatch("PlaceBlock")]
        public static bool Prefix(BuildingBlock _block, Vector2 _gridPosition, BuildingBlockPlacer __instance)
        {
            Log.Info($"baseParent of buildingBLockPlacer: ${__instance.transform.localScale}");
        

            Vector3 position = new Vector3(_gridPosition.x, _gridPosition.y, Main.GridManager.GridPositionZ);

            BuildingBlock buildingBlockTESTCOPY = UnityEngine.Object.Instantiate(_block, position, Quaternion.identity);
            Log.Info($"{JSONSTUFF.Serialize(buildingBlockTESTCOPY)}");

            Log.Info($"placedBlock ${buildingBlockTESTCOPY.GetPivotPosition}");
            return true;
        }


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
        public static bool Prefix(SavedBuildingBlock _block, BuildingBlockPlacer __instance)
        {
            var _dummyBlock = _block;
            Log.Info($"NUMBER 5 IS ALIVE!");
            SaveGameProcess.SavedBuildingBlockExtended savedBlockExtended = SaveGameProcess.SaveGameManagerPatcher.CurrentlyRecreatingSavedBuildingBlockExtended;
            BuildingBlock blockByID = Main.SaveGameManager.buildingBlockLibrary.GetBlockByID(savedBlockExtended.blockID);
            if (blockByID != null)
            {
                BuildingBlock buildingBlock = UnityEngine.Object.Instantiate(blockByID);
                Log.Info($"_block.flippedY: {savedBlockExtended.FlippedY.ToString()}");
                Log.Info($"Before recreating saved block is: {JSONSTUFF.Serialize(savedBlockExtended)}");

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

                // TODO: figure out why flipping alternates sometimes when loading same file over and over.....
                Log.Info($"Recreated saved block: {buildingBlock.flipParent.localScale}");

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


