using UnityEngine;
using System.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using Odessa.Nets.Core;
using System.Threading.Tasks;
using Odessa.Nets.EntityTracking;
using UnityEngine.Networking;
using Odessa.Core;
using Odessa.Nets.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.UnityConverters.Math;
using static OdessaEngine.NETS.Core.NetsEntity;
using System.Text;
using System.Xml.Serialization;
using Odessa.Core.Models.Enums;
using System.Net;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace OdessaEngine.NETS.Core {
    [ExecuteInEditMode]
    public class NetsNetworking : MonoBehaviour {
        private const string NETS_AUTH_TOKEN = "NETS_AUTH_TOKEN";
        //Hooks
        public static Action<RoomState> JoinRoomResponse;
        public static Action<List<RoomState>> GetAllRoomsResponse;
        public static Action<RoomState> CreateRoomResponse;
        public static Action<AuthResponse> UserTokenResponse;
        public static Action<Guid> OnJoinedRoom;
        public static Action<Guid> OnLeaveRoom;
        public static Action<RoomState> OnCreateRoom;
        public static Action<MatchMakingResponse> OnMatchMakingSuccess;

        public static Action<Guid,int> OnPlayersInRoomLeft;
        public static Action<Guid,int> OnPlayersInRoomJoined;

        public static MatchMakingResponse CurrentMatchMaking { get; set; } = null;
        public static List<Guid> RoomsJoined = new List<Guid>();

        [Range(0, 500)]
        public float DebugLatency = 0f;

        private NETSSettings _settings;
        public NETSSettings settings {
            get {
                if (!_settings)
                    LoadOrCreateSettings();
                return _settings;
            }
        }
        private static AuthResponse currentAuth;
        private static long refreshTokenAt = -1;
        private static bool gettingAuth = false;

        private IEnumerator EnsureAuth() {
            var needsToken = currentAuth == null;
            var needsRefresh = DateTimeOffset.Now.ToUnixTimeSeconds() > refreshTokenAt;
            if ((needsToken || needsRefresh) == false) yield break;

            if (needsToken || needsRefresh) {
                while (gettingAuth) yield return new WaitForSecondsRealtime(0.1f);

                needsToken = currentAuth == null;
                needsRefresh = DateTimeOffset.Now.ToUnixTimeSeconds() > refreshTokenAt;
                if ((needsToken || needsRefresh) == false) yield break;

                gettingAuth = true;

                if (!instance.settings.KeepReferenceOfAccount) {
                    PlayerPrefs.SetString(NETS_AUTH_TOKEN, default);
                }

                if (needsToken) {
                    var cache = PlayerPrefs.GetString(NETS_AUTH_TOKEN, default);
                    if (cache != default && cache.Length > 0) {
                        currentAuth = JsonConvert.DeserializeObject<AuthResponse>(PlayerPrefs.GetString(NETS_AUTH_TOKEN, default));
                    } else {
                        var webRequest = UnityWebRequest.Get($"{authUrl}/createAnonUser?applicationGuid={settings.ApplicationGuid}");
                        yield return webRequest.SendWebRequest();
                        HandleAuthResponse(webRequest, webRequest.downloadHandler.text);
                        needsRefresh = false;
                    }
                }
                
                if (needsRefresh) {
                    var webRequest = UnityWebRequest.Get($"{authUrl}/refresh?applicationGuid={settings.ApplicationGuid}&refreshToken={currentAuth.refreshToken}");

                    yield return webRequest.SendWebRequest();
                    HandleAuthResponse(webRequest, webRequest.downloadHandler.text);
                }

                gettingAuth = false;
            }
        }

        private NETSNetworkedTypesLists _typedLists;
        private NETSNetworkedTypesLists typedLists {
            get {
                if (!_typedLists)
                    _typedLists = GetTypedList();
                return _typedLists;
            }
        }
        private NetsObjectPool<GameObject> NetsObjectPool = new NetsObjectPool<GameObject>();

        private static NETSNetworkedTypesLists GetTypedList() {
            var settings = Resources.Load("NETSNetworkedTypesLists") as NETSNetworkedTypesLists;
#if UNITY_EDITOR
            if (!settings) {
                var scriptable = ScriptableObject.CreateInstance<NETSNetworkedTypesLists>();
                AssetDatabase.CreateFolder("Assets", "Resources");
                AssetDatabase.CreateAsset(scriptable, "Assets/Resources/NETSNetworkedTypesLists.asset");
                EditorUtility.SetDirty(scriptable);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                settings = Resources.Load("NETSNetworkedTypesLists") as NETSNetworkedTypesLists;
            }
#endif
            return settings;
        }

        string url { get {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (settings.UseLocalConnectionInUnity) return "http://127.0.0.1:8001";
#endif
                return NetsNetworkingConsts.NETS_ROOM_SERVICE_URL; 
            }
        }
        string authUrl {
            get {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (settings.UseLocalConnectionInUnity) return "http://127.0.0.1:8002";
#endif
                return NetsNetworkingConsts.NETS_AUTH_SERVICE_URL;
            }
        }

        static NetsNetworking() {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> {
                    new Vector2Converter(),
                    new Vector2IntConverter(),
                    new Vector3Converter(),
                    new Vector3IntConverter(),
                    new Vector4Converter(),
                    new ColorConverter(),
                    new Color32Converter(),
                    new QuaternionConverter(),
                    new Vector2Converter(),
                    new Vector2IntConverter(),
                    new Vector3Converter(),
                    new Vector3IntConverter(),
                }
            };
        }

        //Hooks
        public static Action<bool> OnConnect;
        public static Action<bool> OnIsServer;

        //public GameObject frameworkPlaceholder;
        //public bool showPlaceholderPrefabs = false;

        public bool IsServer = false;
        public static Guid? myAccountGuid { get; set; } = null;
        public static Guid? currentRoom = null;

        WebSocket w;

        public bool canSend => w?.isConnected == true;

        public static NetsNetworking instance;

        bool oldConnected = false;

        public const string AssignedGuidFieldName = ".AssignedGuid";
        public static Dictionary<string, NetsEntity> KnownServerSingletons = new Dictionary<string, NetsEntity>();

        public bool CreateFromGameObject(NetsEntity entity) {
            if (!canSend) return false;
            if (currentRoom.HasValue == false) return false;
            entity.localModel.Owner = entity.Authority.IsServerOwned() ? new Guid() : (myAccountGuid ?? Guid.NewGuid());

            entity.roomGuid = currentRoom.Value;
            if (string.IsNullOrEmpty(entity.localModel.PrefabName)) 
                throw new Exception("Unable to create prefab " + entity.localModel.PrefabName + "," + entity.prefab + "," + entity.gameObject.name);
            //print("Sending entity creation for " + entity.prefab);
            WriteEntityDelta(entity, entity.localModel);
            return true;
        }

        public bool DestroyEntity(ulong id) {
            if (!canSend) return false;
            if (currentRoom.HasValue == false) return false;
            //print("Sending entity destroy for " + id);
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.KeyPairEntityEvent);
                bos.WriteUnsignedZeroableFibonacci(1);
                bos.WriteBitData(bos2 => {
                    bos2.WriteUnsignedZeroableFibonacci(id);
                    bos2.WriteBool(true); // Removed
                });
            }));
            return true;
        }
        private IEnumerator DelayPing(Guid requestId) {
            yield return new WaitForSeconds(1);
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.Pong);
                bos.WriteGuid(requestId);
            }));
        }
        public void SendPong(Guid requestId) {
            /*
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            StartCoroutine(DelayPing(requestId));
            return;
#endif*/
#if UNITY_EDITOR
            if (Application.isPlaying == false) return;
#endif
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.Pong);
                bos.WriteGuid(requestId);
            }));
        }

        public void SendRoomEvent(Guid roomGuid, string eventString= "") {
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.RoomEvent);
                bos.WriteGuid(roomGuid);
                bos.WriteString(eventString);
            }));
        }
        public void SendEntityEventByCreationGuid(Guid roomGuid, Guid creationGuid, string eventString = "") {
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.EntityEventByCreationGuid);
                bos.WriteGuid(roomGuid);
                bos.WriteGuid(creationGuid);
                bos.WriteString(eventString);
            }));
        }
        public void SendEntityEvent(Guid roomGuid, ulong entityId, string eventString = "") {
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.EntityEvent);
                bos.WriteGuid(roomGuid);
                bos.WriteUnsignedZeroableFibonacci(entityId);
                bos.WriteString(eventString);
            }));
        }
        public void SendOwnershipChange(Guid roomGuid, ulong entityId, Guid newOwner) {
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.ChangeOwner);
                bos.WriteGuid(roomGuid);
                bos.WriteUnsignedZeroableFibonacci(entityId);
                bos.WriteGuid(newOwner);
            }));
        }

        public void WriteEntityDelta(NetsEntity e, KeyPairEntity entity) {
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.KeyPairEntityEvent);
                bos.WriteUnsignedZeroableFibonacci(1);
                bos.WriteBitData(bos2 => {
                    bos2.WriteUnsignedZeroableFibonacci(e.Id);
                    bos2.WriteBool(false); // Not removed
                    entity.FlushChanges(bos2);
                });
            }));
        }

        public void Awake() {
            if (!Application.isPlaying) return;
            DontDestroyOnLoad(gameObject);

            if (instance != null) {
                Debug.LogError("Trying to create second Instance of NetsNetworking");
                Destroy(gameObject);
                return;
            }
            instance = this;

            if (!_settings) LoadOrCreateSettings();
        }

		public void Start() {
            if (Application.isPlaying == false) return;

            if (settings.HitWorkerDirectly) {
                StartCoroutine(connect($"{(settings.DebugWorkerUrlAndPort.Contains(":125") ? "wss" : "ws")}://{settings.DebugWorkerUrlAndPort}"));
                StartCoroutine(WaitUntilConnected(() => {
                    var sendData = BitUtils.ArrayFromStream(bos => {
                        bos.WriteByte((byte)ClientToWorkerMessageType.JoinRoom);
                        bos.WriteGuid(Guid.ParseExact(settings.DebugRoomGuid, "N"));
                    });
                    w.Send(sendData);
                }));
                return;
            }
            if (settings.KeepReferenceOfAccount) StartCoroutine(EnsureAuth());
            if (settings.AutomaticRoomLogic) InternalJoinAnyRoom();
        }

        private bool LoadOrCreateSettings() {
            _settings = Resources.Load("NETSSettings") as NETSSettings;
#if UNITY_EDITOR
            if(!_settings) {
                var scriptable = ScriptableObject.CreateInstance<NETSSettings>();
                AssetDatabase.CreateFolder("Assets", "Resources");
                AssetDatabase.CreateAsset(scriptable, "Assets/Resources/NETSSettings.asset");
                EditorUtility.SetDirty(scriptable);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                _settings = Resources.Load("NETSSettings") as NETSSettings;
            }
#endif
            return true;
        }

        bool connected = false;
        public static Dictionary<Guid, KeyPairEntityCollector> keyPairEntityCollectors = new Dictionary<Guid, KeyPairEntityCollector>();
        public static Dictionary<Guid, Dictionary<ulong, NetsEntity>> entityIdToNetsEntity = new Dictionary<Guid, Dictionary<ulong, NetsEntity>>();

        bool initializedSingletons = false;
        bool HasJoinedRoom = false;
        IEnumerator HandlePacketWithDelay(byte[] data) {
            if (DebugLatency > 0) yield return new WaitForSeconds(DebugLatency/1000f);
            HandlePacket(data);
        }

        void HandlePacket(byte[] data) {
            try {
                var bb = new BitBuffer(data);
                var category = bb.getByte();
                //if (category != (byte)ProxyToUnityMessageType.Ping)
                //print($"Got category: {category}");

                if (category == (byte)WorkerToClientMessageType.Ping) {
                    Guid requestId = bb.ReadGuid();
                    SendPong(requestId);
                } else if (category == (byte)WorkerToClientMessageType.JoinedRoom) {
                    var roomGuid = bb.ReadGuid();
                    if (settings.DebugConnections)
                        Debug.Log($"NETS - Joined Room ID {roomGuid}");
                    myAccountGuid = bb.ReadGuid();
                    currentRoom = roomGuid;
                    keyPairEntityCollectors[roomGuid] = new KeyPairEntityCollector();
                    entityIdToNetsEntity[roomGuid] = new Dictionary<ulong, NetsEntity>();
                    keyPairEntityCollectors[roomGuid].AfterEntityCreated = (entity) => {
                        try {
                            if (settings.DebugConnections) print($"Created entity {entity.Id}: {entity.PrefabName}");
                            if (entity.Fields.ContainsKey(AssignedGuidFieldName)) {
                                var guid = Guid.ParseExact(entity.GetString(AssignedGuidFieldName), "N");
                                NetsEntity.NetsEntityByCreationGuidMap.TryGetValue(guid, out var matchedEntity);
                                if (matchedEntity != null) {
                                    entityIdToNetsEntity[roomGuid].Add(entity.Id, matchedEntity);
                                    matchedEntity.OnCreatedOnServer(roomGuid, entity);
                                    matchedEntity.Owner = entity.Owner;
                                    return Task.CompletedTask;
                                }
                            }

                            if (entity.PrefabName == "?ClientConnection") {
                                OnPlayersInRoomJoined?.Invoke(Guid.ParseExact(entity.GetString("AccountGuid"), "N"), PlayersInRoom().Count);
                            }
                            NetworkedTypesLookup.TryGetValue(entity.PrefabName, out var typeToCreate);
                            if (typeToCreate == null && entity.PrefabName.Contains("?") == false) {
                                print("Unable to find object " + entity.Id + " " + entity.PrefabName);
                                return Task.CompletedTask;
                            }

                            if (IsServer && KnownServerSingletons.ContainsKey(typeToCreate.name)) {
                                DestroyEntity(entity.Id);
                                throw new Exception($"Did not create new {typeToCreate.name} as we are server and already have one!");
                            }

                            typeToCreate.prefab.SetActive(false);
                            var newGo = Instantiate(typeToCreate.prefab, new Vector3(0, 0, 0), Quaternion.Euler(0, 0, 0));
                            typeToCreate.prefab.SetActive(true);

                            newGo.GetComponentsInChildren<NetsBehavior>().ToList().ForEach(b => b.TryInitialize());
                            var component = newGo.GetComponent<NetsEntity>();
                            if (component.Authority == AuthorityEnum.ServerSingleton) KnownServerSingletons[typeToCreate.name] = component;
                            entityIdToNetsEntity[roomGuid].Add(entity.Id, component);
                            component.OnCreatedOnServer(roomGuid, entity);
                            newGo.SetActive(true);
                        } catch (Exception e) {
                            Debug.LogWarning(e);
                        }
                        return Task.CompletedTask;
                    };
                    keyPairEntityCollectors[roomGuid].AfterEntityUpdated = async (entity) => {
                        try {
                            //if (settings.DebugConnections) print($"After updated entity {entity.Id}");
                            await Task.CompletedTask;
                        } catch (Exception e) {
                            Debug.LogError(e);
                        }
                    };
                    keyPairEntityCollectors[roomGuid].AfterChangeStreamApplied = async () => {
                        try {
                            if (IsServer && !initializedSingletons) {
                                foreach (var s in typedLists.ServerSingletonsList)
                                    if (KnownServerSingletons.ContainsKey(s.name) == false) {
                                        Debug.Log("Init prefab for singleton");
                                        Instantiate(s.prefab);
                                    }
                                initializedSingletons = true;
                            }
                            if (HasJoinedRoom == false) {
                                OnJoinedRoom?.Invoke(roomGuid);
                                HasJoinedRoom = true;
                            }
                            await Task.CompletedTask;
                        } catch (Exception e) {
                            Debug.LogError(e);
                        }
                    };
                    keyPairEntityCollectors[roomGuid].AfterKeyChanged = async (entity, field) => {
                        try {
                            //print($"room: {roomGuid:N} Updated {entity.Id}.{entity.PrefabName}: [{field.Name}] => {field.Value}");
                            if (entity.Id == 1) {
                                if (entity.PrefabName != "?Room") throw new Exception("Expected room as entity ID 1");
                                if (field.Name == "ServerAccount") {
                                    IsServer = myAccountGuid == Guid.ParseExact(entity.GetString("ServerAccount"), "N");
                                    if (settings.DebugConnections) print($"ServerAccount: {entity.GetString("ServerAccount")} ({(IsServer ? "" : "not ")}me)");

                                    if (IsServer == false) {
                                        var startingEnts = FindObjectsOfType<NetsEntity>();
                                        var localServerEntities = startingEnts
                                            .Where(e => e.Authority.IsServerOwned() && e.Id == 0)
                                            .ToList();
                                        if (settings.DebugConnections) {
                                            print($"Found {localServerEntities.Count} server entities to destroy as we are not server. {string.Join(",", localServerEntities.Select(e => e.prefab))}");
                                        }
                                        localServerEntities.ForEach(e => {
                                            var comp = e.GetComponent<NetsEntity>();
                                            comp.MarkAsDestroyedByServer(); // Avoid throwing
                                            KnownServerSingletons.Remove(comp.prefab);
                                            Destroy(e.gameObject);
                                        });
                                    }
                                    OnIsServer?.Invoke(IsServer);
                                }
                                return;
                            }
                            if (entity.PrefabName.StartsWith("?") == false) {
                                if (entityIdToNetsEntity.TryGetValue(roomGuid, out var roomDict)) {
                                    if (roomDict.TryGetValue(entity.Id, out var e)) {
                                        e.OnFieldChange(entity, field.Name);
                                    } else {
                                        print("Unknown id: " + entity.Id);
                                    }
                                } else {
                                    print("Unknown room: " + roomGuid);
                                }
                            }
                            await Task.CompletedTask;
                        } catch (Exception e) {
                            Debug.LogError(e);
                        }
                    };
                    keyPairEntityCollectors[roomGuid].AfterEntityRemoved = async (entity) => {
                        try {
                            if (settings.DebugConnections) print($"Removed {entity.Id} {entity.PrefabName}");
                            if (entityIdToNetsEntity[roomGuid].TryGetValue(entity.Id, out var e) && e != null && e.gameObject != null) {
                                e.MarkAsDestroyedByServer();
                                Destroy(e.gameObject);
                            }
                            entityIdToNetsEntity[roomGuid].Remove(entity.Id);
                            if (entity.PrefabName == "?ClientConnection") {
                                OnPlayersInRoomLeft?.Invoke(Guid.ParseExact(entity.GetString("AccountGuid"), "N"), PlayersInRoom().Count);
                            }
                            await Task.CompletedTask;
                        } catch (Exception e) {
                            Debug.LogError(e);
                        }
                    };
                    RoomsJoined.Add(roomGuid);

                } else if (category == (byte)WorkerToClientMessageType.KeyPairEntityEvent) {
                    var roomGuid = bb.ReadGuid();
                    //print($"Got entity change for room {roomGuid:N}");
                    try {
                        keyPairEntityCollectors[roomGuid].ApplyDelta(bb, false);
                    } catch (Exception e) {
                        Debug.LogError(e);
                    }
                } else if (category == (byte)WorkerToClientMessageType.RoomEvent) {
                    var roomGuid = bb.ReadGuid();
                    var accountGuid = bb.ReadGuid();
                    var eventString = bb.ReadString();
                } else if (category == (byte)WorkerToClientMessageType.EntityEvent) {
                    var roomGuid = bb.ReadGuid();
                    var senderAccountGuid = bb.ReadGuid();
                    var entityId = bb.ReadUnsignedZeroableFibonacci();
                    var eventString = bb.ReadString();
                    try {
                        if (entityIdToNetsEntity[roomGuid].TryGetValue(entityId, out var nets)) {
                            if (nets)
                                nets.InterpretMethod(eventString);
                        } else {
                            Debug.LogError($"Nets Entity doesn't exist. Room {roomGuid:N}, Entity{entityId}");
                        }
                    } catch (Exception e) {
                        Debug.LogError(e);
                    }
                } else if (category == (byte)WorkerToClientMessageType.LeftRoom) {
                } else if (category == (byte)WorkerToClientMessageType.ChangeOwner) {
                    var roomGuid = bb.ReadGuid();
                    var senderAccountGuid = bb.ReadGuid();
                    var entityId = bb.ReadUnsignedZeroableFibonacci();
                    var newOwner = bb.ReadGuid();
                    try {
                        if (entityIdToNetsEntity[roomGuid].TryGetValue(entityId, out var nets)) {
                            if (nets)
                                nets.localModel.Owner = newOwner;
                        } else {
                            Debug.LogError($"Nets Entity doesn't exist. Room {roomGuid:N}, Entity{entityId}");
                        }
                    } catch(Exception e) {
                        Debug.LogError(e);
                    }
                }
            } catch (Exception e) {
                OnConnect?.Invoke(false);
                Debug.LogError(e);
            }
        }

        public static List<Guid> PlayersInRoom() {
            if(RoomsJoined.Count > 0 && keyPairEntityCollectors.ContainsKey(RoomsJoined[0]))
                return keyPairEntityCollectors[RoomsJoined[0]].knownEntities
                    .Where(o => o.Value.PrefabName == "?ClientConnection")
                    .Select(e => Guid.ParseExact(e.Value.GetString("AccountGuid"), "N"))
                    .ToList();
            return new List<Guid>();
        }
        

        bool intentionallyDisconnected = false;
		private void Disconnect() {
            Debug.Log("On Disconnect from NETS"); 
            intentionallyDisconnected = true;
            w.Close();

            foreach (var room in entityIdToNetsEntity.Keys) {
                foreach (var e in entityIdToNetsEntity[room].Values) {
                    e.MarkAsDestroyedByServer(); // Avoid throwing
                    KnownServerSingletons.Remove(e.prefab);
                    if(e && e.gameObject)
                        Destroy(e.gameObject);
                }
            }
            NetsEntity.NetsEntityByCreationGuidMap.Clear();
            entityIdToNetsEntity.Clear();
            keyPairEntityCollectors.Clear();
            KnownServerSingletons.Clear();

            foreach (var e in FindObjectsOfType<NetsEntity>()) {
                e.MarkAsDestroyedByServer(); // Avoid throwing
                Destroy(e.gameObject);
            }
            if (RoomsJoined.Count > 0) {
                var roomGuid = RoomsJoined[0];
                RoomsJoined.Remove(roomGuid);
                OnLeaveRoom?.Invoke(roomGuid);
            }
            CurrentMatchMaking = null;

        }
        IEnumerator connect(string url) {
            intentionallyDisconnected = false;
            if (connected) {
                if (settings.DebugConnections)
                    print("Already connected");
                yield break;
            }
            WebSocket conn = new WebSocket(new Uri(url));
            if (settings.DebugConnections) print("Attempting connection to game server on " + url);

            if (w != null || connected) {
                if (settings.DebugConnections) print("Closing websocket");
                conn.Close();
                yield return new WaitForSeconds(0.02f);
            }

            conn = new WebSocket(new Uri(url));

            if (settings.DebugConnections) print("waiting for ready");
            while (conn.isReady == false) {
                yield return new WaitForSeconds(.03f); // Wait for iframe to postback
            }

            if (settings.DebugConnections) print("attempting connect");
            yield return StartCoroutine(conn.Connect());
            if (settings.DebugConnections) print("Connected to game server? " + conn.isConnected);
            yield return new WaitForSeconds(.2f);

            bool valid = true;
            if (!conn.isConnected) {
                yield return new WaitForSeconds(.02f);
                valid = false;
                if (intentionallyDisconnected) yield break;
                if (settings.DebugConnections) print("Attempting reconnect...");
                if (!connected)
                    StartCoroutine(connect(url));
                yield break;
            }
            if (connected) {
                if (settings.DebugConnections) print("Too late for " + url);
                conn.Close(); // Too late
                yield break;
            }

            w = conn;
            connected = true;
            keyPairEntityCollectors.Clear();
            entityIdToNetsEntity.Clear();
            if (settings.DebugConnections) print("Debug: valid: " + valid + " , connected " + connected);

            //listener.OnConnected();
            HasJoinedRoom = false;
            initializedSingletons = false;
            foreach(var ent in GetAllNetsEntitysOfTypes(AuthorityEnum.ServerSingleton)) {
                Destroy(ent.gameObject);
            }
            KnownServerSingletons.Clear();
            while (valid) {
                if (!w.isConnected) {
                    if (!intentionallyDisconnected) print("ws error!");
                    valid = false;
                    connected = false;
                    oldConnected = false;
                    OnConnect?.Invoke(false);
                    if (intentionallyDisconnected) yield break;
                    StartCoroutine(connect(url));
                    yield break;
                }

                if (w.isConnected != oldConnected)
                    OnConnect?.Invoke(w.isConnected);
                oldConnected = w.isConnected;

                byte[] data = w.Recv();
                if (data == null) {
                    yield return new WaitForSeconds(.03f); // Basically just yield to other threads, checking 30 times a sec
                    continue;
                }
                if (DebugLatency > 0)
                    yield return HandlePacketWithDelay(data);
                else
                    HandlePacket(data);

            }
            OnConnect?.Invoke(false);
            w.Close();
        }

        private List<NetsEntity> GetAllNetsEntitysOfTypes(AuthorityEnum authType) {
            return FindObjectsOfType<NetsEntity>().Where( e => e.Authority ==  authType).ToList();
        }
        
        [Serializable]
        public class NetworkObjectConfig {
            public string name;
            public GameObject prefab;
        }


        Dictionary<string, NetworkObjectConfig> _networkedTypesLookup;
        public Dictionary<string, NetworkObjectConfig> NetworkedTypesLookup { 
            get {
                if (_networkedTypesLookup == null) _networkedTypesLookup = typedLists.NetworkedTypesList.ToDictionary(t => t.name, t => t);
                return _networkedTypesLookup;
            }
        }

        /// <summary>
        /// Create and or join a room by Name. This is a Http request so requires a callback when the request is complete.
        /// <para>Logic flow:</para>
        /// <para><b>IF</b> the room exists join it</para>
        /// <para>Else the room does <b>NOT</b> exist create it <b>AND</b> join it</para>
        /// </summary>
        /// 
        /// <remarks>
        /// Basic and fundemental room connection for NETS.
        /// 
        /// Other options are
        /// <seealso cref="NetsNetworking.CreateRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.JoinRoom(string, Action{RoomState})"/>
        /// <seealso cref="NetsNetworking.GetAllRooms(Action{List{RoomState}})"/>
        /// </remarks>
        /// 
        /// <example>
        /// CreateOrJoinRoom("Room1", (RoomState)=>{UIManager.SwapToLobbyUI();}, 30);
        /// </example>
        /// 
        /// <param name="RoomName">Room name to try to Create or Join. Can be anything, does not need to exist. Random Guid string would be an easy way to create generic random rooms</param>
        /// <param name="CallBack">Action called upon successful completion of Method.</param>
        /// <param name="NoPlayerTTL">IF the room is created, how long should the room stay alive with 0 players in it. Reccomended above 0 as 0 may cause issues. Default is 30 seconds.</param>
        /// 
        /// <code>
        /// CreateOrJoinRoom("Room1", (RoomState)=>{UIManager.SwapToLobbyUI();}, 30);
        /// </code>
        /// 
        public static void CreateOrJoinRoom(string RoomName, Action<RoomState> CallBack = null, int NoPlayerTTL = 30) {
            instance.InternalJoinOrCreateRoom(RoomName, CallBack, NoPlayerTTL);
        }

        /// <summary>
        /// Create a room by Name. This is a Http request so requires a callback when the request is complete.
        /// <para>Logic flow:</para>
        /// <para><b>IF</b> the room name <b>DOES NOT</b> exists create it. (This will not automatically join the room)</para>
        /// </summary>
        /// 
        /// <remarks>
        /// Basic and fundemental room connection for NETS.
        /// 
        /// Other options are
        /// <seealso cref="NetsNetworking.CreateOrJoinRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.JoinRoom(string, Action{RoomState})"/>
        /// <seealso cref="NetsNetworking.LeaveRoom(Guid, Action{Guid})"/>
        /// <seealso cref="NetsNetworking.GetAllRooms(Action{List{RoomState}})"/>
        /// </remarks>
        /// 
        /// <example>
        /// CreateRoom("Room2", (RoomState)=>{RoomAvailabliltyManager.AddAvailableRooms(RoomState);}, 30);
        /// </example>
        /// 
        /// <param name="RoomName">Room name to try to Create. Can be any string. Random Guid string would be an easy way to create generic random rooms</param>
        /// <param name="CallBack">Action called upon successful completion of Method.</param>
        /// <param name="NoPlayerTTL">IF the room is created, how long should the room stay alive with 0 players in it. Reccomended above 0 as 0 may cause issues. Default is 30 seconds.</param>
        /// 
        /// <code>
        /// CreateRoom("Room2", (RoomState)=>{RoomAvailabliltyManager.AddAvailableRooms(RoomState);}, 30);
        /// </code>
        /// 
        public static void CreateRoom(string RoomName, Action<RoomState> CallBack = null, int NoPlayerTTL = 30) {
            instance.InternalCreateRoom(RoomName, CallBack, NoPlayerTTL);
        }
        /// <summary>
        /// Join a by Name. This is a Http request so requires a callback when the request is complete.
        /// <para>Logic flow:</para>
        /// <para><b>IF</b> the room name <b>DOES</b> exist <b>THEN</b> join it.</para>
        /// </summary>
        /// 
        /// <remarks>
        /// Basic and fundemental room connection for NETS.
        /// 
        /// Other options are
        /// <seealso cref="NetsNetworking.CreateOrJoinRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.CreateRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.LeaveRoom(Guid, Action{Guid})"/>
        /// <seealso cref="NetsNetworking.GetAllRooms(Action{List{RoomState}})"/>
        /// </remarks>
        /// 
        /// <example>
        /// JoinRoom("Room2", ( RoomState ) => { StartGame(RoomState); });
        /// </example>
        /// 
        /// <param name="RoomName">Room name to try to Join. Can be any string, but must have been created by <see cref="NetsNetworking.CreateRoom(string, Action{RoomState}, int)"/> or by listed by <see cref="NetsNetworking.GetAllRooms(Action{List{RoomState}})"/></param>
        /// <param name="CallBack">Action called upon successful completion of Method.</param>
        /// 
        /// <code>
        /// JoinRoom("Room2", ( RoomState ) => { StartGame(RoomState); });
        /// </code>
        /// 
        public static void JoinRoom(string RoomName, Action<RoomState> CallBack = null) {
            instance.InternalJoinRoom(RoomName, CallBack);
        }
        /// <summary>
        /// Leave room by ID. Will leave the room and call the action after you've successfully left the room.
        /// <para>Logic flow:</para>
        /// <para><b>IF</b> the room ID <b>DOES</b> exist <b>THEN</b> leave it.</para>
        /// </summary>
        /// 
        /// <remarks>
        /// Basic and fundemental room connection for NETS.
        /// 
        /// Other options are
        /// <seealso cref="NetsNetworking.CreateOrJoinRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.CreateRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.LeaveOnlyRoom(Action{Guid})"/>
        /// <seealso cref="NetsNetworking.GetAllRooms(Action{List{RoomState}})"/>
        /// </remarks>
        /// 
        /// <example>
        /// LeaveRoom("Room2", ( RoomGuid ) => { LeftRoom(RoomGuid); });
        /// </example>
        /// 
        /// <param name="RoomGuid">Room Guid to try to Leave. Can be any Guid, but must have been Joined by <see cref="NetsNetworking.JoinRoom(string, Action{RoomState})"/>. Can be found via <see cref="NetsNetworking.RoomsJoined"/></param>
        /// <param name="CallBack">Action called upon successful completion of Method.</param>
        /// 
        /// <code>
        /// LeaveRoom("Room2", ( RoomGuid ) => { LeftRoom(RoomGuid); });
        /// </code>
        /// 
        public static void LeaveRoom(Guid RoomGuid = default) {
            if(RoomGuid == default) {
                LeaveRoom();
                return;
            }
            instance.InternalLeaveRoom(RoomGuid);
        }
        /// <summary>
        /// Leave only room joined. Will leave the only room currently joined and call the action after you've successfully left the room.
        /// <para>Logic flow:</para>
        /// <para><b>IF</b> there is <b>ONLY</b> one room <b>THEN</b> leave it.</para>
        /// 
        /// </summary>
        /// 
        /// <remarks>
        /// Basic and fundemental room connection for NETS.
        /// 
        /// Other options are
        /// <seealso cref="NetsNetworking.CreateOrJoinRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.CreateRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.LeaveRoom(Guid, Action{Guid})"/>
        /// <seealso cref="NetsNetworking.LeaveRoom(Action{Guid})"/>
        /// <seealso cref="NetsNetworking.GetAllRooms(Action{List{RoomState}})"/>
        /// </remarks>
        /// 
        /// <example>
        /// LeaveOnlyRoom(( RoomGuid ) => { LeftRoom(RoomGuid); });
        /// </example>
        /// 
        /// <param name="CallBack">Action called upon successful completion of Method.</param>
        /// 
        /// <code>
        /// LeaveOnlyRoom(( RoomGuid ) => { LeftRoom(RoomGuid); });
        /// </code>
        /// 
        private static bool LeaveRoom() {
            if (RoomsJoined.Count != 1) {
                instance.Disconnect();
                return false;
            }
            instance.InternalLeaveRoom(RoomsJoined.First());
            return true;
        }
        /// <summary>
        /// Get all available rooms. This is a Http request so requires a callback when the request is complete.
        /// </summary>
        /// 
        /// <remarks>
        /// Basic and fundemental room connection for NETS.
        /// 
        /// Other options are
        /// <seealso cref="NetsNetworking.CreateOrJoinRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.CreateRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.LeaveRoom(Guid, Action{Guid})"/>
        /// <seealso cref="NetsNetworking.JoinRoom(string, Action{RoomState})"/>
        /// </remarks>
        /// 
        /// <example>
        /// GetAllRooms(( RoomStates ) => { UiManager.RoomSelection.UpdateList(RoomStates); });
        /// </example>
        /// 
        /// <param name="CallBack">Action called upon successful completion of Method.</param>
        /// 
        /// <code>
        /// GetAllRooms(( RoomStates ) => { UiManager.RoomSelection.UpdateList(RoomStates); });
        /// </code>
        /// 
        public static void GetAllRooms(Action<List<RoomState>> CallBack) {
            instance.InternalGetAllRooms(CallBack);
        }
        /// <summary>
        /// Nets Matchmaking. Pass in settings, update and complete actions and it will handle match making for you.
        /// If you need meta data included see below. You can include a JSON string for objects.
        /// <code>
        /// MatchmakerSettings.Mode
        /// </code>
        /// </summary>
        /// 
        /// <remarks>
        /// Matchmaking for NETS.
        /// 
        /// Other options are
        /// <seealso cref="NetsNetworking.CreateOrJoinRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.CreateRoom(string, Action{RoomState}, int)"/>
        /// <seealso cref="NetsNetworking.LeaveRoom(Guid, Action{Guid})"/>
        /// <seealso cref="NetsNetworking.JoinRoom(string, Action{RoomState})"/>
        /// <seealso cref="NetsNetworking.GetAllRooms(Action{List{RoomState}})"/>
        /// <seealso cref="NetsNetworking.CreateAnonUser(Action{AuthResponse})"/>
        /// </remarks>
        /// 
        /// <example>
        /// StartMatchMaking(
        /// new MatchmakerSettings() {
        ///  MinimumPlayers = 2,
        ///  MaximumPlayers = 10,
        ///  WaitForMaximumPlayersSeconds = 30,
        ///  Mode = "{\"GameMode\":\"Team\"}",
        ///  },
        ///  (response)=>UpdateQueueCount(response.RoomState.playerCount),
        ///  (response)=>EnterGame(),
        /// );
        /// </example>
        /// 
        /// <param name="Settings">Matchmaking settings for the room to be created.</param>
        /// <param name="CallBackOnUpdate">Action called when the update request comes back. While In Queue.</param>
        /// <param name="CallBackOnComplete">Action called when the complete request comes back. When In Game.</param>
        /// 
        /// <code>
        /// StartMatchMaking(
        /// new MatchmakerSettings() {
        ///  MinimumPlayers = 2,
        ///  MaximumPlayers = 10,
        ///  WaitForMaximumPlayersSeconds = 30,
        ///  Mode = "{\"GameMode\":\"Team\"}",
        ///  },
        ///  (response)=>UpdateQueueCount(response.RoomState.playerCount),
        ///  (response)=>EnterGame(),
        /// );
        /// </code>
        /// 
        public static void StartMatchMaking(MatchmakerSettings Settings, Action<MatchMakingResponse> CallBackOnUpdate = null, Action<RoomState> CallBackOnComplete = null) {
            instance.StartCoroutine(instance.InternalMatchMakerRequest(Settings, CallBackOnUpdate, CallBackOnComplete));
        }

        protected bool TryGetObjectFromResponse<T>(UnityWebRequest req, string response, out T obj) {
            obj = default;
            if(req.responseCode == 401) {
                refreshTokenAt = -1;
                return false;
            }
            if (req.responseCode != 200) {
                //This should probably send a notification to our channels via webhook
                Debug.LogError($"NETS Error on server contact devs. Code: {req.responseCode} Error: {response}");
                return false;
            }

            try {
                obj = JsonConvert.DeserializeObject<T>(response);
                return true;
            } catch (Exception e) {
                Debug.LogError($"NETS format error on server contact devs, Error: {e} Response: {response}");
                return false;
            }
        }
        protected IEnumerator InternalMatchMakerRequest(MatchmakerSettings settings, Action<MatchMakingResponse> CallBackOnUpdate = null, Action<RoomState> CallBackOnComplete = null) {
            Dictionary<string, int> regionalPings = new Dictionary<string, int>();
            regionalPings.Add("USE", 0);
            var matchMakingState = MatchMakingState.IN_QUEUE;
            MatchMakingResponse result = default;
            while (matchMakingState != MatchMakingState.IN_GAME) {
                var requestComplete = false;

                yield return EnsureAuth();
                var toUseUrl = $"{url}/matchMakerRequest?accountToken={currentAuth.accessToken}&settings={JsonConvert.SerializeObject(settings)}&pings={JsonConvert.SerializeObject(regionalPings)}";
                var webRequest = UnityWebRequest.Get(toUseUrl);
                var coroutine = StartCoroutine(SendOnWebRequestComplete(webRequest, (resultText) => {
                    if (!TryGetObjectFromResponse(webRequest, resultText, out MatchMakingResponse matchMakingResponse)) {
                        requestComplete = true;
                        return;
                    }
                    matchMakingState = matchMakingResponse.State;
                    CallBackOnUpdate?.Invoke(matchMakingResponse);
                    OnMatchMakingSuccess?.Invoke(matchMakingResponse);
                    CurrentMatchMaking = matchMakingResponse;
                    result = matchMakingResponse;
                    requestComplete = true;
                }));
                yield return new WaitUntil(() => requestComplete);
                yield return new WaitForSeconds(2);
            }
            InternalJoinRoom(result.RoomState, CallBackOnComplete);
        }

        private void HandleAuthResponse(UnityWebRequest webRequest, string resultText) {
            if (!TryGetObjectFromResponse(webRequest, resultText, out AuthResponse authResponse)) throw new Exception("Unable to deserialize AuthResponse");
            currentAuth = authResponse;

            if (instance.settings.KeepReferenceOfAccount) {
                PlayerPrefs.SetString(NETS_AUTH_TOKEN, JsonConvert.SerializeObject(authResponse));
            } else {
                PlayerPrefs.SetString(NETS_AUTH_TOKEN, default);
            }

            var tokenInfo = DecodeJwt(authResponse.accessToken);
            var tokenLifetime = Convert.ToInt64(tokenInfo["exp"]) - Convert.ToInt64(tokenInfo["iat"]);
            refreshTokenAt = DateTimeOffset.Now.ToUnixTimeSeconds() + tokenLifetime / 2;

            UserTokenResponse?.Invoke(authResponse);
        }

        private static Dictionary<string, string> DecodeJwt(string jwt) {
            var parts = jwt.Split('.');
            if (parts.Length != 3) throw new Exception("Not a JWT");
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(Encoding.ASCII.GetString(Convert.FromBase64String(parts[1])));
            
        }
        protected void InternalCreateRoom(string RoomName, Action<RoomState> CallBack = null, int NoPlayerTTL = 30) {
            var webRequest = UnityWebRequest.Get($"{url}/createRoom?token={settings.ApplicationGuid}&roomConfig={JsonUtility.ToJson(new RoomConfigData() { Name = RoomName, ttlNoPlayers = NoPlayerTTL })}");
            StartCoroutine(SendOnWebRequestComplete(webRequest, (resultText) => {
                if (!TryGetObjectFromResponse(webRequest, resultText, out RoomState roomState)) return;
                CallBack?.Invoke(roomState);
                CreateRoomResponse?.Invoke(roomState);
                OnCreateRoom?.Invoke(roomState);
            }));
        }
        protected void InternalJoinRoom(string RoomName, Action<RoomState> CallBack = null) {
            var webRequest = UnityWebRequest.Get($"{url}/joinRoom?token={settings.ApplicationGuid}&roomName={RoomName}");
            StartCoroutine(SendOnWebRequestComplete(webRequest, (resultText) => {
                if (!TryGetObjectFromResponse(webRequest, resultText, out RoomState roomState)) return;
                InternalJoinRoom(roomState, CallBack);
            }));
        }
        protected void InternalJoinRoom(RoomState stateToJoin, Action<RoomState> CallBack = null) {
            StartCoroutine(connect($"{(stateToJoin.ip.Contains(":125") ? "wss" : "ws")}://{stateToJoin.ip}"));
            StartCoroutine(WaitUntilConnected(() => {
                var sendData = BitUtils.ArrayFromStream(bos => {
                    bos.WriteByte((byte)ClientToWorkerMessageType.JoinRoom);
                    bos.WriteGuid(Guid.ParseExact(stateToJoin.token, "N"));
                });
                w.Send(sendData);
            }));
            CallBack?.Invoke(stateToJoin);
            JoinRoomResponse?.Invoke(stateToJoin);
        }
        protected void InternalJoinAnyRoom() {
            InternalGetAllRooms((available) => {
                if (available.Count > 0) {
                    foreach (var av in available) {
                        if (av.playerCount >= 100) {
                            continue;
                        } else {
                            JoinRoom(av.name);
                            return;
                        }
                    }
                    CreateOrJoinRoom(Guid.NewGuid().ToString("N"));
                } else {
                    CreateOrJoinRoom(settings.DefaultRoomName);
                }
            });
        }
        protected void InternalGetAllRooms(Action<List<RoomState>> CallBack) {
            var webRequest = UnityWebRequest.Get($"{url}/listRooms?token={settings.ApplicationGuid}");
            StartCoroutine( SendOnWebRequestComplete( webRequest, (resultText) => {
                if (!TryGetObjectFromResponse(webRequest, resultText, out List<RoomState> roomStates)) return;
                CallBack?.Invoke(roomStates);
                GetAllRoomsResponse?.Invoke(roomStates);
            }));
        }
        protected void InternalJoinOrCreateRoom(string RoomName, Action<RoomState> CallBack = null, int NoPlayerTTL = 30) {
            var webRequest = UnityWebRequest.Get($"{url}/joinOrCreateRoom?token={settings.ApplicationGuid}&roomConfig={JsonUtility.ToJson(new RoomConfigData() { Name = RoomName, ttlNoPlayers = NoPlayerTTL })}");
            StartCoroutine(SendOnWebRequestComplete(webRequest, (resultText) => {
                if (!TryGetObjectFromResponse(webRequest, resultText, out RoomState roomState)) return;
                StartCoroutine(connect($"{(roomState.ip.Contains(":125") ? "wss" : "ws")}://{roomState.ip}"));
                StartCoroutine(WaitUntilConnected(() => {
                    var sendData = BitUtils.ArrayFromStream(bos => {
                        bos.WriteByte((byte)ClientToWorkerMessageType.JoinRoom);
                        bos.WriteGuid(Guid.ParseExact(roomState.token, "N"));
                    });
                    w.Send(sendData);
                }));
                CallBack?.Invoke(roomState);
                CreateRoomResponse?.Invoke(roomState);
                JoinRoomResponse?.Invoke(roomState);
            }));
        }
        protected void InternalLeaveRoom(Guid RoomGuid) {
            var requestGuid = Guid.NewGuid();
            try {
                w.Send(BitUtils.ArrayFromStream(bos => {
                    bos.WriteByte((byte)ClientToWorkerMessageType.LeaveRoom);
                    bos.WriteGuid(RoomGuid);
                    bos.WriteGuid(requestGuid);
                }));
            } catch (Exception e) {
                Debug.LogError(e);
            }
            Disconnect();
        }

        private IEnumerator SendOnWebRequestComplete(UnityWebRequest webRequest, Action<string> onComplete) {
            yield return webRequest.SendWebRequest();
            //TODO handle errors
            onComplete?.Invoke(webRequest.downloadHandler.text);
        }
        private IEnumerator WaitUntilConnected(Action action) {
            while(!connected)
                yield return new WaitForEndOfFrame();
            action?.Invoke();
        }

#if UNITY_ANDROID || UNITY_IOS
        private void OnApplicationPause(bool pause) {
            if (pause) {
                //System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
        }

#endif
    }
}