using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Reflection;

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
		[SerializeField]
		public List<EntitySetting> EntitySettings = new List<EntitySetting>();

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
	public class EntitySetting {
		public string lookup;
		[Range(0.0f, 20.0f)]
		public float SyncFramesPerSecond = 5f;
		public AuthorityEnum Authority = AuthorityEnum.Client;
		public List<ObjectToSync> ObjectsToSync = new List<ObjectToSync>();
	}

	[Serializable]
	public class ObjectToSync {
		public Transform Transform;
		public List<ComponentsToSync> Components;
		public bool IsSelf;
	}

	[Serializable]
	public class ComponentsToSync {
		public string ClassName;
		public bool AllEnabled;
		public OdessaRunWhen UpdateWhen;
		public List<ScriptFieldToSync> Fields;
		public string Path;
	}

	[Serializable]
	public class ScriptFieldToSync {
		public string FieldName;
		public string PathName;
		public bool Enabled;
		public string FieldType;
		public LerpType LerpType = LerpType.None;
	}

	public enum OdessaRunWhen {
		Owned,
		Always
	}

	public class ObjectProperty {
		public object Object { get; set; }
		public PropertyInfo Method { get; set; }
		public ScriptFieldToSync Field { get; set; }
		public object Value() => Method.GetValue(Object);
		public void SetValue(object value) => Method.SetValue(Object, value);
	}


	[Serializable]
    public class NetworkObjectConfig {
        public string name;
        public GameObject prefab;
    }
}