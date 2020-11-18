using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Odessa.Nets.EntityTracking;
using Odessa.NETS;
using System.Linq;
using System;
using System.Reflection;
using System.Linq.Expressions;
using Newtonsoft.Json;
using System.ComponentModel;
using Newtonsoft.Json.UnityConverters.Math;
using Newtonsoft.Json.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class NetsEntity : MonoBehaviour {
    [Header("Sync properties")]
    [Range(0.0f, 20.0f)]
    public float SyncFramesPerSecond = 1f;
    [Header("Fields To Sync")]
    public bool SyncPosition = true;
    public bool SyncRotation = true;
    public bool SyncScale = true;
    public bool SyncAnimation = true;
    public TransformReplicationOverrides SyncTransformOverrides = new TransformReplicationOverrides();

    public bool ServerControlled = false;
    public bool ServerSingleton = false;

    public ulong Id;
    public Guid roomGuid;
    public Guid? creationGuid;
    NetsEntityState state;

    Vector3AdaptiveLerp positionLerp = new Vector3AdaptiveLerp();
    Vector3AdaptiveLerp rotationLerp = new Vector3AdaptiveLerp();
    Vector3AdaptiveLerp scaleLerp = new Vector3AdaptiveLerp();
    [Serializable]
    public struct TransformReplicationOverrides {
        public Transform position;
        public Transform rotation;
        public Transform scale;
        public Transform animation;
    }
    Transform GetPositionTransform() => SyncTransformOverrides.position != null ? SyncTransformOverrides.position : transform; // Using ?? does not work
    Transform GetRotationTransform() => SyncTransformOverrides.rotation != null ? SyncTransformOverrides.rotation : transform; // Using ?? does not work
    Transform GetScaleTransform() => SyncTransformOverrides.scale != null ? SyncTransformOverrides.scale : transform; // Using ?? does not work
    Transform GetAnimationTransform() => SyncTransformOverrides.animation != null ? SyncTransformOverrides.animation : transform; // Using ?? does not work

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
        positionLerp.expectedReceiveDelay = 1f / SyncFramesPerSecond;
        rotationLerp.expectedReceiveDelay = 1f / SyncFramesPerSecond;
        scaleLerp.expectedReceiveDelay = 1f / SyncFramesPerSecond;

        localModel = new KeyPairEntity {
            PrefabName = prefab,
            isNew = true,
        };

        if (ServerSingleton) NetsNetworking.KnownServerSingletons.Add(prefab, this);
    }

    private void TryCreateOnServer() {
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
        var shouldSetFields = state == NetsEntityState.Uninitialized;
        if (shouldSetFields)
            e.Fields.ToList().ForEach(kv => OnFieldChange(e, kv.Key, true));
        else {
            if (OwnedByMe == false && ServerControlled == false) {
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
    public bool OwnedByMe => (state != NetsEntityState.Insync && !ServerControlled) ||
        (ServerControlled && NetsNetworking.instance?.IsServer == true) ||
        (NetsNetworking.myAccountGuid != null && NetsNetworking.myAccountGuid == networkModel?.Owner);
    /// <summary>
    /// Use to check if the local account was the creator of this entity
    /// </summary>
    public Guid Creator => networkModel?.Creator ?? NetsNetworking.myAccountGuid ?? default;
    public Guid Owner => networkModel?.Owner ?? default;
    public bool IsAuthority => OwnedByMe || NetsNetworking.instance?.IsServer == true;

    void OnDestroy() {
#if !UNITY_EDITOR
        if (IsAuthority == false) throw new Exception($"Destroyed entity {prefab} without authority to do so");
        NetsNetworking.instance?.DestroyEntity(Id);
#endif
    }

    public static Dictionary<Type, PropertyInfo[]> fields = new Dictionary<Type, PropertyInfo[]>();

	float lastUpdateTime = 0f;
    bool SetPropertiesBeforeCreation = false;
	public void LateUpdate() {
        if (SetPropertiesBeforeCreation == false) {
            if (state != NetsEntityState.Insync) return;
            if (IsAuthority == false) return;
            if (Time.time < lastUpdateTime + 1f / SyncFramesPerSecond) return;
        }
        lastUpdateTime = Time.time;

        if (OwnedByMe || SetPropertiesBeforeCreation) {
            if (SyncPosition) localModel.SetVector3(".position", GetPositionTransform().position);
            if (SyncRotation) localModel.SetVector3(".rotation", GetRotationTransform().eulerAngles);
            if (SyncScale) localModel.SetVector3(".scale", GetScaleTransform().localScale);

            foreach (var c in transform.GetComponents<ClientNetworked>()) {
                var typeName = c.GetType().Name;
                if (!fields.ContainsKey(c.GetType())) fields[c.GetType()] = c.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var field in fields[c.GetType()]) {
                    var objectToSave = field.GetValue(c);
                    if (objectToSave is Vector2 v2) objectToSave = new System.Numerics.Vector2(v2.x, v2.y);
                    if (objectToSave is Vector3 v3) objectToSave = new System.Numerics.Vector3(v3.x, v3.y, v3.z);
                    localModel.SetObject($".{typeName}.{field.Name}", objectToSave);
                }
            }
        }

        if (NetsNetworking.instance?.IsServer == true) {
            foreach (var c in transform.GetComponents<ServerNetworked>()) {
                var typeName = c.GetType().Name;
                if (!fields.ContainsKey(c.GetType())) fields[c.GetType()] = c.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var field in fields[c.GetType()]) {
                    var objectToSave = field.GetValue(c);
                    if (objectToSave is Vector2 v2) objectToSave = new System.Numerics.Vector2(v2.x, v2.y);
                    if (objectToSave is Vector3 v3) objectToSave = new System.Numerics.Vector3(v3.x, v3.y, v3.z);
                    localModel.SetObject($".{typeName}.{field.Name}", objectToSave);
                }
            }
        }

        foreach (var c in transform.GetComponents<NetworkedBehavior>())
            c.SaveState(localModel);

        if (localModel.IsDirty && SetPropertiesBeforeCreation == false) {
            NetsNetworking.instance.WriteEntityDelta(this, localModel);
        }
    }

    public void OnFieldChange(KeyPairEntity entity, string key, bool force = false) {
        if (this == null) return;
        if (OwnedByMe == false || force) {
            if (key == ".position") {
                var value = entity.GetUnityVector3(key);
                positionLerp.ValueChanged(value);
                if (force) GetPositionTransform().position = value;
            } else if (key == ".rotation") {
                var value = entity.GetUnityVector3(key);
                rotationLerp.ValueChanged(value);
                GetRotationTransform().eulerAngles = value;
            } else if (key == ".scale") {
                var value = entity.GetUnityVector3(key);
                scaleLerp.ValueChanged(value);
                GetScaleTransform().localScale = value;
            }
        }

        foreach (var c in transform.GetComponents<NetworkedBehavior>())
            c.OnFieldUpdate(entity, key);

        if (key.StartsWith(".")) {
            var split = key.Split('.');
            if (split.Length != 3) return;

            if (OwnedByMe == false || force) {
                foreach (var c in transform.GetComponents<ClientNetworked>()) {
                    if (c.GetType().Name != split[1]) continue;
                    if (!fields.ContainsKey(c.GetType())) fields[c.GetType()] = c.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    var field = fields[c.GetType()].SingleOrDefault(f => f.Name == split[2]);
                    if (field != null) {
                        var obj = entity.GetObject(key);
                        if (obj is System.Numerics.Vector2 v2) obj = v2.ToUnityVector2();
                        if (obj is System.Numerics.Vector3 v3) obj = v3.ToUnityVector3();
                        field.SetValue(c, obj);
                    }
                }
            }

            if (NetsNetworking.instance.IsServer == false) {
                foreach (var c in transform.GetComponents<ServerNetworked>()) {
                    if (c.GetType().Name != split[1]) continue;
                    if (!fields.ContainsKey(c.GetType())) fields[c.GetType()] = c.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    var field = fields[c.GetType()].SingleOrDefault(f => f.Name == split[2]);
                    if (field != null) {
                        var obj = entity.GetObject(key);
                        if (obj is System.Numerics.Vector2 v2) obj = v2.ToUnityVector2();
                        if (obj is System.Numerics.Vector3 v3) obj = v3.ToUnityVector3();
                        field.SetValue(c, obj);
                    }
                }
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
                if (SyncPosition) GetPositionTransform().position = positionLerp.GetLerped();
                if (SyncRotation) GetRotationTransform().eulerAngles = rotationLerp.GetLerped();
                if (SyncScale) GetScaleTransform().localScale = scaleLerp.GetLerped();
            } else {
                positionLerp.ValueChanged(GetPositionTransform().position);
                rotationLerp.ValueChanged(GetRotationTransform().eulerAngles);
                scaleLerp.ValueChanged(GetScaleTransform().localScale);
            }

            if (OwnedByMe)
                foreach (var c in transform.GetComponents<NetworkedBehavior>())
                    c.OwnedUpdate();

            if (NetsNetworking.instance.IsServer)
                foreach (var c in transform.GetComponents<NetworkedBehavior>())
                    c.ServerUpdate();

            if (OwnedByMe)
                foreach (var c in transform.GetComponents<ClientNetworked>())
                    c.OwnedUpdate();

            if (NetsNetworking.instance.IsServer)
                foreach (var c in transform.GetComponents<ServerNetworked>())
                    c.ServerUpdate();
        }

#if UNITY_EDITOR
        if (Application.isPlaying) return;

        if (ServerControlled == false && ServerSingleton == true) ServerSingleton = false;

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
            if (comp.GetType() is NetsEntity) continue;
            var type = comp.GetType();
            foreach(var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
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
                if(obj is JObject j) 
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
            if (Id == 0 ) {
                TryCreateOnServer();
                if (!creationGuid.HasValue)
                    throw new Exception($"No creation Guid for NETS Entity Name: {method.DeclaringType.Name}.{method.Name}");
                try {
                    NetsNetworking.instance.SendEntityEventByCreationGuid(roomGuid, creationGuid.Value, JsonConvert.SerializeObject(new MethodEvent { methodId = index, args = args }));
                }catch(Exception e) {
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
    public void RPC<T1, T2, T3, T4, T5, T6, T7, T8>(Action<T1, T2, T3, T4, T5, T6, T7, T8> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => RPC(method.Method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8});
    public void RPC<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) => RPC(method.Method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9});
    public void RPC<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10) => RPC(method.Method, new object[] { arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 });
    public void RPC(Action method, object[] parameters) => RPC(method.Method, parameters);

}
