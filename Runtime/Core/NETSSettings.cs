using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdessaEngine.NETS.Core {
	[CreateAssetMenu(fileName = "NETSSettings", menuName = "NETS/CreateSettingsObject", order = 1)]
	[Serializable]
	public class NETSSettings : ScriptableObject {
		[Header("Project Settings")]
		[SerializeField]
		public string ApplicationGuid = Guid.NewGuid().ToString("N");
		[SerializeField]
		public string DefaultRoomName = "default";
		[SerializeField]
		public bool AutomaticRoomLogic = true;

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
	}
}
