using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static OdessaEngine.NETS.Core.NetsNetworking;

[Serializable]
public class NETSNetworkedTypesLists : ScriptableObject {
    public List<NetworkObjectConfig> NetworkedTypesList = new List<NetworkObjectConfig>();
    public List<NetworkObjectConfig> ServerSingletonsList = new List<NetworkObjectConfig>();
}
