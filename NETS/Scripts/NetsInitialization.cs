#if UNITY_EDITOR
using System.IO;
using UnityEngine;


namespace Odessa.NETS {
    class NetsInitialization {
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded() {
            var settingsFile = $"{Directory.GetCurrentDirectory()}/ProjectSettings/NETS.conf";
            if (!File.Exists(settingsFile)) {
                File.Create(settingsFile).Close();
                if (Object.FindObjectsOfType<NetsNetworking>().Length == 0) {
                    var go = new GameObject("NETS");
                    go.AddComponent<NetsNetworking>();
                }
            }
        }
    }
}
#endif