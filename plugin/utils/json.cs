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

    public static class JSON
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
            resolver.IncludeProperties(typeof(Vector3), "x", "y", "z");
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
            resolver.IncludeProperties(typeof(Vector3), "x", "y", "z");
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
}
            