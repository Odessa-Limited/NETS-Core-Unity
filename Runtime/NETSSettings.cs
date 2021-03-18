using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace OdessaEngine.NETS.Core {
	//[CreateAssetMenu(fileName = "NETSSettings", menuName = "NETS/CreateSettingsObject", order = 1)]
	[Serializable]
	public class NETSSettings : ScriptableObject {
		[Header("Project Settings")]
		[SerializeField]
		public string ApplicationGuid = Guid.NewGuid().ToString("N");
		[SerializeField]
		public string DefaultRoomName = "default";
		[SerializeField]
		public bool AutomaticRoomLogic = true;
		[SerializeField]
		public bool KeepReferenceOfAccount = true;

		[Header("NETS Developer Tools")]
		[SerializeField]
		public bool UseLocalConnectionInUnity = false;
		[SerializeField]
		public bool HitWorkerDirectly = false;
		[SerializeField]
		public string DebugWorkerUrlAndPort = "140.82.41.234:12334";
		[SerializeField]
		public string DebugRoomGuid = "00000000000000000000000000000000";
		[SerializeField]
		public bool DebugConnections = true;
		[SerializeField]
		[Range(0, 500)]
		public float DebugLatency = 0f;

		private static NETSSettings _instance;
		public static NETSSettings instance {
			get {
				if (_instance != null) return _instance;

				_instance = Resources.Load("NETSSettings") as NETSSettings;
#if UNITY_EDITOR
				if (!_instance) {
					var scriptable = ScriptableObject.CreateInstance<NETSSettings>();
					AssetDatabase.CreateFolder("Assets", "Resources");
					AssetDatabase.CreateAsset(scriptable, "Assets/Resources/NETSSettings.asset");
					EditorUtility.SetDirty(scriptable);
					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh();
					_instance = Resources.Load("NETSSettings") as NETSSettings;
				}
#endif
				return _instance;
			}
		}

	}
}
