using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdessaEngine.NETS.Core {
	[CreateAssetMenu(fileName = "NETSEntityDefinitions", menuName = "NETS/CreateEntityDefinitions", order = 1)]
	[Serializable]
	public class NETSEntityDefinitions : ScriptableObject {
		[Header("Nets Entity GuidMap")]
		[SerializeField]
		public List<NETSEntityGUIDDefinition> GuidMap = new List<NETSEntityGUIDDefinition>();
		public static List<NETSEntityGUIDDefinition> GuidMapFromDict(Dictionary<int,Guid> dict) {
			var retList = new List<NETSEntityGUIDDefinition>();
			foreach(var vk in dict) {
				retList.Add(new NETSEntityGUIDDefinition() { instanceID = vk.Key, assignedGuid = vk.Value.ToString("N") });
			}
			return retList;
		}
	}
	[Serializable]
	public class NETSEntityGUIDDefinition {
		public int instanceID;
		public string assignedGuid;
	}
}
