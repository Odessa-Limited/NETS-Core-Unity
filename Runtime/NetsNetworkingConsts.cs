using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class NetsNetworkingConsts {
    public static string NETS_ROOM_SERVICE_URL { 
        get {
            return "https://nets.odessaengine.com/nets-room-service";
        }
    }
    public static string NETS_AUTH_SERVICE_URL {
        get {
            return "https://nets.odessaengine.com/nets-account-service";
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
