using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static OdessaEngine.NETS.Core.NetsEntity;
using static OdessaEngine.NETS.Core.NetsNetworking;

namespace OdessaEngine.NETS.Core {
    public static class NetsInitialization {

#if UNITY_EDITOR
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void CreateAssetWhenReady() {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) {
                EditorApplication.delayCall += CreateAssetWhenReady;
                return;
            }

            EditorApplication.delayCall += OnRuntimeMethodLoad;
        }
        [RuntimeInitializeOnLoadMethod]
        static void OnRuntimeMethodLoad() {
            if (UnityEngine.Object.FindObjectsOfType<NetsNetworking>().Length == 0) {
                var go = new GameObject("NETS");
                go.AddComponent<NetsNetworking>();
            }
            var lists = GetTypedList();
            lists.NetworkedTypesList = new List<NetworkObjectConfig>();
            lists.ServerSingletonsList = new List<NetworkObjectConfig>();
            SaveTypedList(lists);


            var allPaths = AssetDatabase.GetAllAssetPaths();
            foreach (var path in allPaths) {
                UnityEngine.Object loaded = null;
                try {
                    loaded = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                } catch {
                    continue;
                }
                if (!loaded)
                    continue;
                if (loaded.GetType() != typeof(GameObject)) continue;
                var asGo = loaded as GameObject;

                var networkedComponentList = asGo.GetComponents<NetsEntity>().ToList();
                if (networkedComponentList.Count == 0) continue;
                if (networkedComponentList.Count > 1) {
                     Debug.LogError("Entity " + path + " has two NetsEntity components");
                    continue;
                }
                var networkedComponent = networkedComponentList.Single();
                networkedComponent.SyncProperties();
                EditorUtility.SetDirty(networkedComponent);
                //if (networkedComponent.GetType().Name != asGo.name) throw new Exception("Name mismatch - Gameobject " + asGo.name + " has networked class " + networkedComponent.GetType().Name);
                if (lists.NetworkedTypesList.Any(n => n.name == networkedComponent.GetType().Name)) continue;
                networkedComponent.prefab = asGo.name;
                lists.NetworkedTypesList.Add(new NetworkObjectConfig {
                    name = asGo.name,
                    prefab = asGo,
                });
                if (networkedComponent.Authority == AuthorityEnum.ServerSingleton) {
                    lists.ServerSingletonsList.Add(new NetworkObjectConfig {
                        name = asGo.name,
                        prefab = asGo,
                    });
                }
                SaveTypedList(lists);
            }
        }
#endif
        private static NETSNetworkedTypesLists GetTypedList() {
            var settings = Resources.Load("NETSNetworkedTypesLists") as NETSNetworkedTypesLists;
#if UNITY_EDITOR
            if (!settings) {
                var scriptable = ScriptableObject.CreateInstance<NETSNetworkedTypesLists>();
                AssetDatabase.CreateFolder("Assets", "Resources");
                AssetDatabase.CreateAsset(scriptable, "Assets/Resources/NETSNetworkedTypesLists.asset");
                EditorUtility.SetDirty(scriptable);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                settings = Resources.Load("NETSNetworkedTypesLists") as NETSNetworkedTypesLists;
            }
#endif
            return settings;
        }
        private static bool SaveTypedList(NETSNetworkedTypesLists list) {
            var settings = Resources.Load("NETSNetworkedTypesLists") as NETSNetworkedTypesLists;
#if UNITY_EDITOR
            if (!settings) {
                AssetDatabase.CreateFolder("Assets", "Resources");
                AssetDatabase.CreateAsset(list, "Assets/Resources/NETSNetworkedTypesLists.asset");
            }
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
#endif
            return true;
        }
    }
}