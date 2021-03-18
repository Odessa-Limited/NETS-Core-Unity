using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.SocialPlatforms;
using Odessa.Nets.EntityTracking.Wrappers;
using Odessa.Nets.EntityTracking.EventObjects.NativeTypes;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor;
#endif

namespace OdessaEngine.NETS.Core {
    [ExecuteAlways]
    public class NetsEntity : MonoBehaviour {
        public static Dictionary<Guid, NetsEntity> NetsEntityByUniqueIdMap = new Dictionary<Guid, NetsEntity>();

        public List<ObjectToSync> ObjectsToSync = new List<ObjectToSync>();

        [Range(0.0f, 20.0f)]
        public float SyncFramesPerSecond = 1f;

        public AuthorityEnum Authority;

        bool destroyedByServer = false;
        public void MarkAsDestroyedByServer() => destroyedByServer = true;

        bool hasStarted = false;

        EntityModel localModel;
        EntityModel networkModel;
        public EntityModel Model() => IsOwnedByMe ? localModel : networkModel;

        [HideInInspector]
        public string prefab;

        public bool IsReady => localModel != null;

        public Guid GetEntityGuid() {
            if (Authority == AuthorityEnum.Client) return Guid.NewGuid();
            if (Authority == AuthorityEnum.Server) return IntToGuid(GetInstanceID());
            return IntToGuid(prefab.GetHashCode());
        }

        static Guid IntToGuid(int value) {
            byte[] bytes = new byte[16];
            BitConverter.GetBytes(value).CopyTo(bytes, 0);
            return new Guid(bytes);
        }

        private void Awake() {
#if UNITY_EDITOR
            if (Application.isPlaying == false) return;
#endif
            // Deal with assignedGuids. 
            /*
            Comes online:
            Client: Always generate
            Server: Never generate
            Singleton: Never generate
             */

            if (Authority == AuthorityEnum.ServerSingleton && NetsNetworking.KnownServerSingletons.ContainsKey(prefab) == false) NetsNetworking.KnownServerSingletons.Add(prefab, this);

            //Nets entitys don't get destroyed when changing scene
            DontDestroyOnLoad(gameObject);

            TryCreateOnServer();
        }

        void TryCreateOnServer() {
            if (IsReady) return;
            if (NetsNetworking.instance?.canSend != true) return;
            if (NetsNetworking.room == null) return;
            if (NetsNetworking.myAccountGuid == null) return;

            var entityGuid = GetEntityGuid();
            localModel = new EntityModel(new DictionaryType(), entityGuid.ToString("N"), NetsNetworking.room);
            NetsEntityByUniqueIdMap.Add(entityGuid, this);
            StorePropertiesToModel();

            //NetsNetworking.instance?.CreateFromGameObject(this);
        }

        public void OnCreatedOnServer(Guid roomGuid, EntityModel e) {
            networkModel = e;
            var shouldSetFields = IsOwnedByMe == false;
            if (shouldSetFields)

                foreach (var t in ObjectsToSync)
                    foreach (var c in t.Components)
                        foreach (var f in c.Fields)
                            OnFieldChange(e.Fields, f.PathName, true);
            else {
                if (IsOwnedByMe == false && Authority == AuthorityEnum.Client) {
                    print("Expected object to have owner as me");
                }
            }
            NetsStart();
        }

        private void NetsStart() {
            if (hasStarted) return;
            foreach (var c in transform.GetComponentsInChildren<NetsBehavior>()) {
                c.NetsStart();
                if (IsOwnedByMe) c.NetsOwnedStart();
            }
            hasStarted = true;
        }

        void Start() {
#if UNITY_EDITOR
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null) {
                PrefabUtility.prefabInstanceUpdated += (a) => {
                    if (a == this)
                        NetsInitialization.OnRuntimeMethodLoad();
                };
                SyncProperties();
            }
            if (Application.isPlaying == false) return;
#endif
            NetsStart();
        }

        /// <summary>
        /// Use to check if the local account is the owner of this entity
        /// OR 
        /// If this is the server and the server owns this
        /// </summary>
        public bool IsOwnedByMe => (networkModel == null && Authority == AuthorityEnum.Client) ||
            (Authority.IsServerOwned() && NetsNetworking.instance?.IsServer == true) ||
            (NetsNetworking.myAccountGuid != null && NetsNetworking.myAccountGuid == networkModel?.Owner);

        /// <summary>
        /// Use to check if the local account was the creator of this entity
        /// </summary>
        public Guid Creator => networkModel?.Creator ?? NetsNetworking.myAccountGuid ?? default;

        public Guid Owner { get => Model().Owner; set => Model().Owner = value; }

        void OnDestroy() {
#if UNITY_EDITOR
            if (Application.isPlaying == false) return;
#endif
            if (IsOwnedByMe == false && destroyedByServer == false) {
                Debug.LogWarning($"Destroyed entity {prefab} without authority to do so");
                return;
            }
            if (destroyedByServer == false) {
                localModel.Remove();
            }
        }

        public void OnApplicationQuit() {
            destroyedByServer = true;
        }

        public Dictionary<string, ObjectProperty> pathToProperty = new Dictionary<string, ObjectProperty>();
        public Dictionary<string, Vector3LerpingObjectProperty> pathToLerpVector3 = new Dictionary<string, Vector3LerpingObjectProperty>();
        public Dictionary<string, QuaternionLerpingObjectProperty> pathToLerpQuaternion = new Dictionary<string, QuaternionLerpingObjectProperty>();
        HashSet<string> loggedUnknownPaths = new HashSet<string>();
        public ObjectProperty GetPropertyAtPath(string path) {
            if (pathToProperty.TryGetValue(path, out var r)) return r;
            foreach (var t in ObjectsToSync) {
                foreach (var c in t.Components) {
                    foreach (var f in c.Fields) {
                        try {
                            if (f.PathName == path) {
                                var component = t.Transform.GetComponents<Component>().SingleOrDefault(com => com.GetType().Name == c.ClassName);
                                if (component == null) throw new Exception("unknown component for path " + path);
                                var method = component.GetType().GetValidNetsProperties(t.IsSelf).SingleOrDefault(prop => prop.Name == f.FieldName);
                                if (method == null) throw new Exception("unknown method for path " + path);
                                var objProp = new ObjectProperty {
                                    Object = component,
                                    Method = method,
                                    Field = f,
                                };
                                pathToProperty[path] = objProp;
                                return objProp;
                            }
                        } catch (Exception e) {
                            if (loggedUnknownPaths.Contains(f.PathName)) return null;
                            Debug.LogError("Unable to get property at path " + path + ". Error: " + e);
                            loggedUnknownPaths.Add(f.PathName);
                        }
                    }
                }
            }
            return null;
        }

        float lastUpdateTime = 0f;

        public void StorePropertiesToModel() {
            foreach (var t in ObjectsToSync) {
                foreach (var c in t.Components) {
                    foreach (var f in c.Fields) {
                        if (f.Enabled == false) continue;
                        var objProp = GetPropertyAtPath(f.PathName);
                        if (objProp == null) {
                            print("Unable to get property at path: " + f.PathName + " - it's null!");
                            continue;
                        }
                        var objectToSave = objProp.Value();
                        if (objectToSave.GetType().IsNetsNativeType()) {
                            localModel.Fields.SetObject(f.PathName, objectToSave);
                        } else {
                            localModel.Fields.SetString(f.PathName, JsonConvert.SerializeObject(objectToSave));
                        }
                    }
                }
            }
        }

        public void LateUpdate() {
            // Wait for activation
            if (!IsReady) {
                TryCreateOnServer();
                return;
            }

            if (!IsOwnedByMe) return;
            if (Time.time < lastUpdateTime + 1f / SyncFramesPerSecond) return;
            lastUpdateTime = Time.time;

            StorePropertiesToModel();
        }

        public void OnFieldChange(DictionaryModel fields, string key, bool force = false) {
            if (this == null) return;
            if (IsOwnedByMe == false || force) {
                if (key.StartsWith(".")) {
                    var objProp = GetPropertyAtPath(key);

                    var obj = fields.GetObject(key, objProp.Method.PropertyType);
                    if (objProp.Method.PropertyType.IsNetsNativeType()) {
                        obj = JsonConvert.DeserializeObject((string)obj, objProp.Method.PropertyType);
                    }

                    // Check lerps
                    if (objProp.Field.LerpType != LerpType.None) {
                        if (objProp.Field.FieldType == "Vector3") {
                            if (!pathToLerpVector3.TryGetValue(key, out var lerpObj)) {
                                lerpObj = pathToLerpVector3[key] = new Vector3LerpingObjectProperty {
                                    Field = objProp.Field,
                                    Object = objProp.Object,
                                    Method = objProp.Method,
                                    Lerp = new Vector3AdaptiveLerp(),
                                };
                                lerpObj.Lerp.expectedReceiveDelay = 1 / SyncFramesPerSecond;
                                lerpObj.Lerp.type = objProp.Field.LerpType;
                                lerpObj.SetValue((Vector3)obj);
                            }
                            lerpObj.Lerp.ValueChanged((Vector3)obj);
                            return;
                        } else if (objProp.Field.FieldType == "Quaternion") {
                            if (!pathToLerpQuaternion.TryGetValue(key, out var lerpObj)) {
                                lerpObj = pathToLerpQuaternion[key] = new QuaternionLerpingObjectProperty {
                                    Field = objProp.Field,
                                    Object = objProp.Object,
                                    Method = objProp.Method,
                                    Lerp = new QuaternionAdaptiveLerp(),
                                };
                                lerpObj.Lerp.expectedReceiveDelay = 1 / SyncFramesPerSecond;
                                lerpObj.Lerp.type = objProp.Field.LerpType;
                                lerpObj.SetValue((Quaternion)obj);
                            }
                            lerpObj.Lerp.ValueChanged((Quaternion)obj);
                            return;
                        }

                    }

                    // Else set property directly
                    objProp.SetValue(obj);
                }
            }
        }

        public static Guid Int2Guid(int value) {
            byte[] bytes = new byte[16];
            BitConverter.GetBytes(value).CopyTo(bytes, 0);
            return new Guid(bytes);
        }

        bool lastOwnedByMe = false;
        void Update() {
            /*
#if UNITY_EDITOR
            if (Application.isPlaying == false) {
                if (gameObject.IsPrefab()) {
                    EditorSceneManager.MarkSceneDirty(gameObject.scene);
                    EditorUtility.SetDirty(gameObject);
                } else if (gameObject.IsInPrefabIsolationContext()) {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(this);
                    EditorSceneManager.MarkSceneDirty(gameObject.scene);
                    EditorUtility.SetDirty(gameObject);
                }
            }
#endif
            */

            if (Application.isPlaying) {
                // Ownership change
                var owned = IsOwnedByMe;
                var ownershipChanged = IsOwnedByMe != lastOwnedByMe;
                if (ownershipChanged) {
                    // TODO RESET LERPS
                    if (owned) { // Gained control
                        if (networkModel != null) localModel = networkModel.Clone();

                        foreach (var c in transform.GetComponentsInChildren<NetsBehavior>())
                            c.NetsOnGainOwnership();

                        foreach (var lo in pathToLerpVector3.Values) lo.SetValue(lo.Lerp.GetMostRecent());
                        foreach (var lo in pathToLerpQuaternion.Values) lo.SetValue(lo.Lerp.GetMostRecent());
                    } else {
                        localModel = null;
                        foreach (var c in transform.GetComponentsInChildren<NetsBehavior>())
                            c.NetsOnLostOwnership();

                        foreach (var lo in pathToLerpVector3.Values) {
                            lo.Lerp.Reset(1 / SyncFramesPerSecond, (Vector3)lo.Value());
                            lo.Lerp.ValueChanged((Vector3)lo.Value());
                        }
                        foreach (var lo in pathToLerpQuaternion.Values) {
                            lo.Lerp.Reset(1 / SyncFramesPerSecond, (Quaternion)lo.Value());
                            lo.Lerp.ValueChanged((Quaternion)lo.Value());
                        }
                    }
                    lastOwnedByMe = owned;
                }

                // Run through lerps
                if (!owned) {
                    foreach (var lo in pathToLerpVector3.Values) lo.SetValue(lo.Lerp.GetLerped());
                    foreach (var lo in pathToLerpQuaternion.Values) lo.SetValue(lo.Lerp.GetLerped());
                }

                foreach (var c in transform.GetComponentsInChildren<NetsBehavior>()) {
                    c.NetsUpdate();
                    if (IsOwnedByMe)
                        c.NetsOwnedUpdate();
                }
            }
            //Shouldn't need to sync every frame
            SyncProperties();
        }
        private Dictionary<MethodInfo, ulong> methodToIdLookup = new Dictionary<MethodInfo, ulong>();
        private Dictionary<ulong, MethodInfo> idToMethodLookup = new Dictionary<ulong, MethodInfo>();
        ulong methodIndex = 0;
        private void SetUpMethodDict() {
            if (methodToIdLookup.Count > 0)
                return;
            // Get the public methods.
            // We can garuntee that get components will return the same order every time https://answers.unity.com/questions/1293957/reliable-order-of-components-using-getcomponents.html
            foreach (var comp in gameObject.GetComponents<MonoBehaviour>()) {
                if (comp is NetsEntity) continue;
                var type = comp.GetType();
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                    var index = methodIndex;
                    idToMethodLookup.Add(index, method);
                    methodToIdLookup.Add(method, index);
                    methodIndex++;
                }
            }
        }
        public void SyncProperties() {
#if UNITY_EDITOR
            if (Application.isPlaying) return;
            if (!PrefabUtility.IsPartOfAnyPrefab(gameObject) && PrefabUtility.GetPrefabInstanceStatus(gameObject) == PrefabInstanceStatus.NotAPrefab && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(gameObject)) && PrefabUtility.GetPrefabAssetType(gameObject) == PrefabAssetType.NotAPrefab && string.IsNullOrEmpty(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(this)) && PrefabStageUtility.GetCurrentPrefabStage() == null) {
                Debug.LogError($"{gameObject.name} object needs to be a prefab for NetsEntity script to function");
                return;
            }
            if (PrefabUtility.GetPrefabAssetType(gameObject) != PrefabAssetType.NotAPrefab || PrefabStageUtility.GetCurrentPrefabStage() != null || !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(gameObject)) || !string.IsNullOrEmpty(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(this))) {
                var longPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(this);
                if (string.IsNullOrEmpty(longPath))
                    longPath = PrefabStageUtility.GetCurrentPrefabStage().assetPath;
                if (string.IsNullOrEmpty(longPath))
                    longPath = AssetDatabase.GetAssetPath(gameObject);
                if (string.IsNullOrEmpty(longPath))
                    longPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(this);
                var split = longPath.Split('/');
                var prefabName = split.Last();
                var prefabSplit = prefabName.Split('.');
                var final = prefabSplit.First();
                prefab = final;

                // Fill in Objects to sync
                if (ObjectsToSync.Any(o => o.Transform == transform) == false)
                    ObjectsToSync.Insert(0, new ObjectToSync {
                        Transform = transform,
                        Components = new List<ComponentsToSync>(),
                    });

                ObjectsToSync.ForEach(o => o.IsSelf = false);
                ObjectsToSync[0].IsSelf = true;

                foreach (var obj in ObjectsToSync) {
                    var components = obj.Transform.GetComponents<Component>();

                    foreach (var comp in components) {
                        if (comp is NetsEntity) continue;

                        var componentToSync = obj.Components.FirstOrDefault(f => f.ClassName == comp.GetType().Name);
                        if (componentToSync == null) {
                            componentToSync = new ComponentsToSync {
                                ClassName = comp.GetType().Name,
                                AllEnabled = false,
                                Fields = new List<ScriptFieldToSync>(),
                            };
                            obj.Components.Add(componentToSync);
                        }

                        var componentFields = new List<ScriptFieldToSync>();
                        var props = comp.GetType().GetValidNetsProperties(obj.IsSelf);

                        foreach (var p in props) {
                            var propToSync = componentToSync.Fields.FirstOrDefault(f => f.FieldName == p.Name);
                            if (propToSync == null) {
                                propToSync = new ScriptFieldToSync {
                                    FieldName = p.Name,
                                    PathName = "." + (obj.IsSelf ? this.prefab : obj.Transform.name) + "." + comp.GetType().Name + "." + p.Name,
                                    Enabled = true,
                                    LerpType = LerpType.Velocity,
                                };
                                componentToSync.Fields.Add(propToSync);
                            }
                            propToSync.FieldType = p.PropertyType.Name;
                            propToSync.PathName = "." + (obj.IsSelf ? this.prefab : obj.Transform.name) + "." + comp.GetType().Name + "." + p.Name;
                        }
                        componentToSync.Fields = componentToSync.Fields.Where(f => props.Any(p => p.Name == f.FieldName)).ToList();
                    }
                    obj.Components = obj.Components.Where(f => components.Any(c => c.GetType().Name == f.ClassName)).ToList();
                    EditorUtility.SetDirty(gameObject);
                }
            }
#endif
        }
        public void InterpretMethod(string MethodEvent) {
            if (destroyedByServer) return;//Don't run on ents that are flagged as dead
            SetUpMethodDict();
            var e = JsonConvert.DeserializeObject<MethodEvent>(MethodEvent);
            if (idToMethodLookup.TryGetValue(e.methodId, out var method)) {
                ParameterInfo[] _params = method.GetParameters();
                var typedParams = _params.Select((p, i) => {
                    var obj = e.args[i];
                    if (obj is JObject j)
                        return j.ToObject(p.ParameterType);
                    return Convert.ChangeType(obj, p.ParameterType);
                }).ToList();
                Component comp = GetComponent(method.DeclaringType);
                if (comp)
                    method.Invoke(comp, typedParams.ToArray());
                else
                    Debug.Log($"Component doesn't exist {comp.name}");
            } else
                throw new Exception($"Received RPC that we don't have a method for {e.methodId}");
        }
        protected class MethodEvent {
            public object[] args;
            public ulong methodId;
        }
        private void RPC(MethodInfo method, object[] args) {
            //Set dependencies
            SetUpMethodDict();
            if (IsOwnedByMe) {
                method.Invoke(GetComponent(method.DeclaringType), args);
                return;
            }
            if (methodToIdLookup.TryGetValue(method, out var index)) {
                TryCreateOnServer();
                try {
                    NetsNetworking.instance.SendEntityEvent(this, JsonConvert.SerializeObject(new MethodEvent { methodId = index, args = args }));
                } catch (Exception e) {
                    Debug.LogError(e);
                }
            } else
                Debug.LogError($"No method matching  {method.DeclaringType.Name}.{method.Name}");
        }
        public void RPC(Action method) => RPC(method.Method, new object[] { });
        public void RPC<T1>(Action<T1> method, T1 arg1) => RPC(method.Method, new object[] { arg1 });
        public void RPC<T1, T2>(Action<T1, T2> method, T1 arg1, T2 arg2) => RPC(method.Method, new object[] { arg1, arg2 });
        public void RPC<T1, T2, T3>(Action<T1, T2, T3> method, T1 arg1, T2 arg2, T3 arg3) => RPC(method.Method, new object[] { arg1, arg2, arg3 });
        public void RPC<T1, T2, T3, T4>(Action<T1, T2, T3, T4> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => RPC(method.Method, new object[] { arg1, arg2, arg3, arg4 });
        public void RPC<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => RPC(method.Method, new object[] { arg1, arg2, arg3, arg4, arg5 });
        public void RPC<T1, T2, T3, T4, T5, T6>(Action<T1, T2, T3, T4, T5, T6> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) => RPC(method.Method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6 });
        public void RPC<T1, T2, T3, T4, T5, T6, T7>(Action<T1, T2, T3, T4, T5, T6, T7> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) => RPC(method.Method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7 });
        public void RPC<T1, T2, T3, T4, T5, T6, T7, T8>(Action<T1, T2, T3, T4, T5, T6, T7, T8> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => RPC(method.Method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 });
        public void RPC<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) => RPC(method.Method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 });
        public void RPC<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10) => RPC(method.Method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 });
        public void RPC(Action method, object[] parameters) => RPC(method.Method, parameters);
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

    public class Vector3LerpingObjectProperty : ObjectProperty {
        public Vector3AdaptiveLerp Lerp { get; set; }
    }
    public class QuaternionLerpingObjectProperty : ObjectProperty {
        public QuaternionAdaptiveLerp Lerp { get; set; }
    }
}
