using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Odessa.Nets.EntityTracking;
using System.Linq;
using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace OdessaEngine.NETS.Core {
    [ExecuteInEditMode]
    public class NetsEntity : MonoBehaviour {
        public List<ObjectToSync> ObjectsToSync = new List<ObjectToSync>();
        public Transform addedTransform;

        [Header("Sync properties")]
        [Range(0.0f, 20.0f)]
        public float SyncFramesPerSecond = 1f;

        public enum AuthorityEnum {
            Client,
            Server,
            ServerSingleton,
        }
        public AuthorityEnum Authority;

        public ulong Id;
        public Guid roomGuid;
        public Guid? creationGuid;
        NetsEntityState state;
        public bool destroyedByServer = false;

        private static PropertyInfo[] GetValidPropertiesFor(Type t, bool isTopLevel) => t
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(p => p.GetAccessors().Length == 2)
            .Where(p => !p.GetGetMethod().IsStatic)

            .Where(p => t != typeof(Transform) || new string[] {
                isTopLevel ? nameof(Transform.position) : nameof(Transform.localPosition),
                isTopLevel ? nameof(Transform.eulerAngles) : nameof(Transform.localEulerAngles), 
                nameof(Transform.localScale)
            }.Contains(p.Name))

            .Where(p => t != typeof(Rigidbody2D) || new string[] {
                nameof(Rigidbody2D.velocity),
                nameof(Rigidbody2D.angularVelocity),
                nameof(Rigidbody2D.mass),
                //nameof(Rigidbody2D.drag), // linearDrag in webGL :C
                nameof(Rigidbody2D.angularDrag)
            }.Contains(p.Name))

            .Where(p => TypedField.SyncableTypeLookup.ContainsKey(p.PropertyType) || new []{ typeof(Vector2), typeof(Vector3) }.Contains(p.PropertyType))
            .ToArray();

        public enum NetsEntityState {
            Uninitialized,
            Pending,
            Insync
        }

        public KeyPairEntity localModel;
        KeyPairEntity networkModel;

        public static Dictionary<Guid, NetsEntity> NetsEntityByCreationGuidMap = new Dictionary<Guid, NetsEntity>();
        static Dictionary<string, NetsEntity> NetsEntityByRoomAndIdMap = new Dictionary<string, NetsEntity>();

        [HideInInspector]
        public string prefab;

        private void Awake() {
#if UNITY_EDITOR
            if (Application.isPlaying == false) return;
#endif

            localModel = new KeyPairEntity {
                PrefabName = prefab,
                isNew = true,
            };

            if (Authority == AuthorityEnum.ServerSingleton) NetsNetworking.KnownServerSingletons.Add(prefab, this);
        }

        private void TryCreateOnServer() {
            if (NetsNetworking.instance?.canSend != true) return;
            if (destroyedByServer) return;
            if (state != NetsEntityState.Uninitialized) return;
            if (creationGuid == null) {
                creationGuid = Guid.NewGuid();
                localModel.SetString(NetsNetworking.CreationGuidFieldName, creationGuid.Value.ToString("N"));
                NetsEntityByCreationGuidMap.Add(creationGuid.Value, this);
                SetPropertiesBeforeCreation = true;
                LateUpdate();
                SetPropertiesBeforeCreation = false;
            }
            if (NetsNetworking.instance?.CreateFromGameObject(this) == true) {
                //print("Asked server to create " + prefab);
                state = NetsEntityState.Pending;
            }
        }

        public void OnCreatedOnServer(Guid roomGuid, KeyPairEntity e) {
            networkModel = e;
            Id = e.Id;
            this.roomGuid = roomGuid;
            NetsEntityByRoomAndIdMap[roomGuid.ToString() + Id] = this;
            var shouldSetFields = OwnedByMe == false;
            if (shouldSetFields)
                e.Fields.ToList().ForEach(kv => OnFieldChange(e, kv.Key, true));
            else {
                if (OwnedByMe == false && Authority == AuthorityEnum.Client) {
                    print("Expected object to have owner as me");
                }
            }
            state = NetsEntityState.Insync;
        }

        void Start() {
#if UNITY_EDITOR
            if (Application.isPlaying == false) return;
#endif
            StartCoroutine(createOnServer());
        }

        /// <summary>
        /// Use to check if the local account is the owner of this entity
        /// OR 
        /// If this is the server and the server owns this
        /// </summary>
        public bool OwnedByMe => (state != NetsEntityState.Insync && Authority == AuthorityEnum.Client) ||
            (Authority.IsServerOwned() && NetsNetworking.instance?.IsServer == true) ||
            (NetsNetworking.myAccountGuid != null && NetsNetworking.myAccountGuid == networkModel?.Owner);

        /// <summary>
        /// Use to check if the local account was the creator of this entity
        /// </summary>
        public Guid Creator => networkModel?.Creator ?? NetsNetworking.myAccountGuid ?? default;
        public Guid Owner => networkModel?.Owner ?? default;

        void OnDestroy() {
#if UNITY_EDITOR
            if (Application.isPlaying == false) return;
#endif
            if (OwnedByMe == false && destroyedByServer == false) throw new Exception($"Destroyed entity {prefab} without authority to do so");
            if (destroyedByServer == false) {
                NetsNetworking.instance?.DestroyEntity(Id);
            }
        }

        public Dictionary<string, ObjectProperty> pathToProperty = new Dictionary<string, ObjectProperty>();
        public Dictionary<string, Vector3LerpingObjectProperty> pathToLerp = new Dictionary<string, Vector3LerpingObjectProperty>();
        public ObjectProperty GetPropertyAtPath(string path) {
            if (pathToProperty.TryGetValue(path, out var r)) return r;
            foreach (var t in ObjectsToSync) {
                foreach (var c in t.Components) {
                    foreach (var f in c.Fields) {
                        try {
                            if (f.PathName == path) {
                                var component = t.Transform.GetComponents<Component>().SingleOrDefault(com => com.GetType().Name == c.ClassName);
                                if (component == null) throw new Exception("unknown component for path " + path);
                                var method = GetValidPropertiesFor(component.GetType(), t.IsSelf).SingleOrDefault(prop => prop.Name == f.FieldName);
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
                            Debug.LogError("Unable to get property at path " + path + ". Error: " + e);
                        }
                    }
                }
            }
            return null;
        }

        float lastUpdateTime = 0f;
        bool SetPropertiesBeforeCreation = false;
        public void LateUpdate() {
            if (SetPropertiesBeforeCreation == false) {
                if (state != NetsEntityState.Insync) return;
                if (OwnedByMe == false) return;
                if (Time.time < lastUpdateTime + 1f / SyncFramesPerSecond) return;
            }
            lastUpdateTime = Time.time;

            if (OwnedByMe || SetPropertiesBeforeCreation) {
                foreach (var t in ObjectsToSync) {
                    foreach (var c in t.Components) {
                        foreach (var f in c.Fields) {
                            if (f.Enabled == false) continue;
                            var objProp = GetPropertyAtPath(f.PathName);
                            var objectToSave = objProp.Value();
                            if (objectToSave is Vector2 v2) objectToSave = new System.Numerics.Vector2(v2.x, v2.y);
                            if (objectToSave is Vector3 v3) objectToSave = new System.Numerics.Vector3(v3.x, v3.y, v3.z);
                            localModel.SetObject(f.PathName, objectToSave);
                        }

                    }

                }
            }

            if (localModel.IsDirty && SetPropertiesBeforeCreation == false) {
                NetsNetworking.instance.WriteEntityDelta(this, localModel);
            }
        }

        public void OnFieldChange(KeyPairEntity entity, string key, bool force = false) {
            if (this == null) return;
            if (OwnedByMe == false || force) {
                if (key.StartsWith(".")) {
                    var objProp = GetPropertyAtPath(key);
                    if (objProp == null) {
                        if (key != NetsNetworking.CreationGuidFieldName) Debug.Log("Unable to find path: " + key);
                        return;
                    }

                    var obj = entity.GetObject(key);
                    if (obj is System.Numerics.Vector2 v2) obj = v2.ToUnityVector2();
                    if (obj is System.Numerics.Vector3 v3) obj = v3.ToUnityVector3();

                    // Check lerps
                    if (objProp.Field.FieldType == "Vector3" && objProp.Field.LerpType != LerpType.None) {
                        if (!pathToLerp.TryGetValue(key, out var lerpObj)) {
                            lerpObj = pathToLerp[key] = new Vector3LerpingObjectProperty {
                                Field = objProp.Field,
                                Object = objProp.Object,
                                Method = objProp.Method,
                                Lerp = new Vector3AdaptiveLerp(),
                            };
                            lerpObj.Lerp.expectedReceiveDelay = 1 / SyncFramesPerSecond;
                            lerpObj.Lerp.type = objProp.Field.LerpType;
                        }
                        lerpObj.Lerp.ValueChanged((Vector3)obj);
                        return;
                    }

                    // Else set property directly
                    objProp.SetValue(obj);
                }
            }
        }

        IEnumerator createOnServer() {
            while (true) {
                if (state != NetsEntityState.Insync) {
                    TryCreateOnServer();
                    yield return new WaitForSeconds(1f / 2f);
                    continue;
                } else
                    break;
            }
        }

        void Update() {

            if (Application.isPlaying) {
                //if (NetsNetworking.instance == null) return;
                //if (NetsNetworking.instance.canSend == false) return;

                if (!OwnedByMe && state == NetsEntityState.Insync) {
                    // Run through lerps
                    //if (SyncPosition) GetPositionTransform().position = positionLerp.GetLerped();
                    foreach (var lo in pathToLerp.Values) {
                        lo.SetValue(lo.Lerp.GetLerped());
                    }
                } else {
                    foreach (var c in transform.GetComponentsInChildren<NetsBehavior>())
                        c.NetsUpdate();
                }
            }

#if UNITY_EDITOR
            if (Application.isPlaying) return;

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            this.prefab = prefab?.name;
            if (prefab == null)
                Debug.LogError($"{gameObject.name} object needs to be a prefab for NetsEntity script to function");
            else {
                var component = prefab.GetComponent<NetsEntity>();
                if (component == null) {
                    component = prefab.AddComponent<NetsEntity>();
                    var go = gameObject;
                    DestroyImmediate(this);
                    Selection.activeObject = prefab;
                }
                component.prefab = prefab.name;
            }

            // Fill in Objects to sync
            if (ObjectsToSync.Any(o => o.Transform == transform) == false)
                ObjectsToSync.Insert(0, new ObjectToSync {
                    Transform = transform,
                    Components = new List<ComponentsToSync>(),
                });

            foreach (var obj in ObjectsToSync) {
                obj.IsSelf = obj.Transform == transform;
                var components = obj.Transform.GetComponents<Component>();

                foreach (var comp in components) {
                    if (comp is NetsEntity) continue;

                    var componentToSync = obj.Components.FirstOrDefault(f => f.ClassName == comp.GetType().Name);
                    if (componentToSync == null) {
                        componentToSync = new ComponentsToSync {
                            ClassName = comp.GetType().Name,
                            Fields = new List<ScriptFieldToSync>(),
                        };
                        obj.Components.Add(componentToSync);
                    }

                    var componentFields = new List<ScriptFieldToSync>();
                    var props = GetValidPropertiesFor(comp.GetType(), obj.IsSelf);

                    foreach (var p in props) {
                        var propToSync = componentToSync.Fields.FirstOrDefault(f => f.FieldName == p.Name);
                        if (propToSync == null) {
                            propToSync = new ScriptFieldToSync {
                                FieldName = p.Name,
                                //PathName = "." + obj.Transform + "." + comp.GetType().Name + "." + p.Name,
                                Enabled = true,
                                LerpType = p.Name.ToLowerInvariant().Contains("angles") ? LerpType.SphericalLinear : LerpType.Velocity,
                            };
                            componentToSync.Fields.Add(propToSync);
                        }
                        propToSync.FieldType = p.PropertyType.Name;
                        propToSync.PathName = "." +  (obj.IsSelf ? this.prefab : obj.Transform.name) + "." + comp.GetType().Name + "." + p.Name;
                    }
                    componentToSync.Fields = componentToSync.Fields.Where(f => props.Any(p => p.Name == f.FieldName)).ToList();
                }
                obj.Components = obj.Components.Where(f => components.Any(c => c.GetType().Name == f.ClassName)).ToList();
            }

#endif
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
        public void InterpretMethod(string MethodEvent) {
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
                method.Invoke(GetComponent(method.DeclaringType), typedParams.ToArray());
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
            if (OwnedByMe) {
                method.Invoke(GetComponent(method.DeclaringType), args);
                return;
            }
            if (methodToIdLookup.TryGetValue(method, out var index)) {
                if (Id == 0) {
                    TryCreateOnServer();
                    if (!creationGuid.HasValue)
                        throw new Exception($"No creation Guid for NETS Entity Name: {method.DeclaringType.Name}.{method.Name}");
                    try {
                        NetsNetworking.instance.SendEntityEventByCreationGuid(roomGuid, creationGuid.Value, JsonConvert.SerializeObject(new MethodEvent { methodId = index, args = args }));
                    } catch (Exception e) {
                        Debug.LogError(e);
                    }
                } else {
                    NetsNetworking.instance.SendEntityEvent(roomGuid, Id, JsonConvert.SerializeObject(new MethodEvent { methodId = index, args = args }));
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
}