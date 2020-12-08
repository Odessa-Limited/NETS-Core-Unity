using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class NetsNetworkingConsts {
    public static int NETS_ROOM_SERVICE_PORT = 8001;
    public static string NETS_URL { 
        get {
            return (NETS_USING_SECURE ? "https" : "http") + "://roomservice.nets.odessaengine.com" + ( NETS_USING_SECURE ? "" : ":" + NETS_ROOM_SERVICE_PORT );
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
