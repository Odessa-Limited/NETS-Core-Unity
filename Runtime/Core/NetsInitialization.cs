#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OdessaEngine.NETS.Core {
    public class NetsInitialization {

        [RuntimeInitializeOnLoadMethod]
        static void OnRuntimeMethodLoad() {
            if (Object.FindObjectsOfType<NetsNetworking>().Length == 0) {
                var go = new GameObject("NETS");
                go.AddComponent<NetsNetworking>();
                
            }
        }
    }
}
#endif