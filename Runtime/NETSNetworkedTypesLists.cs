using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace OdessaEngine.NETS.Core {
    [Serializable]
    public class NETSNetworkedTypesLists : ScriptableObject {
        public List<NetworkObjectConfig> NetworkedTypesList = new List<NetworkObjectConfig>();
        public List<NetworkObjectConfig> ServerSingletonsList = new List<NetworkObjectConfig>();

        private static NETSNetworkedTypesLists _instance;
        public static NETSNetworkedTypesLists instance {
            get {
                if (_instance != null) return _instance;

                _instance = Resources.Load("NETSNetworkedTypesLists") as NETSNetworkedTypesLists;
#if UNITY_EDITOR
                if (!_instance) {
                    var scriptable = CreateInstance<NETSNetworkedTypesLists>();
                    AssetDatabase.CreateFolder("Assets", "Resources");
                    AssetDatabase.CreateAsset(scriptable, "Assets/Resources/NETSNetworkedTypesLists.asset");
                    EditorUtility.SetDirty(scriptable);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    _instance = Resources.Load("NETSNetworkedTypesLists") as NETSNetworkedTypesLists;
                }
#endif
                return _instance;
            }
        }

        private Dictionary<string, NetworkObjectConfig> _networkedTypesLookup;
        public Dictionary<string, NetworkObjectConfig> NetworkedTypesLookup {
            get {
                if (_networkedTypesLookup != null) return _networkedTypesLookup;
                _networkedTypesLookup = instance.NetworkedTypesList.ToDictionary(t => t.name, t => t);
                return _networkedTypesLookup;
            }
        }

    }


    [Serializable]
    public class NetworkObjectConfig {
        public string name;
        public GameObject prefab;
    }
}