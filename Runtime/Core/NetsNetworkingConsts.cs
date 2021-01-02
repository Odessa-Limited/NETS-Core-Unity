using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class NetsNetworkingConsts {
    public static string NETS_URL { 
        get {
            return "https://netstest.odessaengine.com/nets-room-service";
        }
    }
    public static bool NETS_USING_SECURE { 
        get {
#if UNITY_WEBGL
            if (!Application.absoluteURL.StartsWith("https://")) return false;
#endif
            return true;
        }
    }
}
