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

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace OdessaEngine.NETS.Core {
    [ExecuteInEditMode]
    public class NetsNetworking : MonoBehaviour {
        //Hooks
        public static Action<RoomState> JoinRoomResponse;
        public static Action<List<RoomState>> GetAllRoomsResponse;
        public static Action<RoomState> CreateRoomResponse;
        public static Action<Guid> OnJoinedRoom;
        public static Action<Guid> OnJoinedLeft;
        public static Action<RoomState> OnCreateRoom;

        public static Action<int> PlayerCount;
        public static List<Guid> RoomsJoined = new List<Guid>();

        [Range(0, 500)]
        public float DebugLatency = 0f;

        private NETSSettings _settings;
        private NETSSettings settings {
            get{
                if (!_settings)
                    LoadOrCreateSettings();
                return _settings;
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
#if UNITY_EDITOR
                if (settings.UseLocalConnectionInUnity) return "http://127.0.0.1:8001";
#endif
                return NetsNetworkingConsts.NETS_URL; 
            } }

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
        public static Guid? myAccountGuid;
        public static Guid? currentRoom = null;

        WebSocket w;

        public bool canSend => w?.isConnected == true;

        public static NetsNetworking instance;

        bool oldConnected = false;

        List<string> ips = new List<string>();

        public const string CreationGuidFieldName = ".CreationGuid";
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
        public void SendPong(Guid requestId) {
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
            LoadOrCreateSettings();
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

		// Use this for initialization
		public IEnumerator Start() {
            if (!Application.isPlaying) yield break;
            if (!_settings)
                LoadOrCreateSettings();
            instance = this;

            //GameInstance.placeholderEntities = showPlaceholderPrefabs;

            // Get IP list and connect to them all ( try both http and https, we don't know what we are using )
            print("Getting servers");


            //ips.Add("ws://127.0.0.1:" + port);
            //ips.Add("wss://" + URL + ":" + (port + 1000));
            if (settings.HitWorkerDirectly) {
                StartCoroutine(connect($"{(settings.DebugWorkerUrlAndPort.Contains(":125") ? "wss" : "ws")}://{settings.DebugWorkerUrlAndPort}"));
                StartCoroutine(WaitUntilConnected(() => {
                    var sendData = BitUtils.ArrayFromStream(bos => {
                        bos.WriteByte((byte)ClientToWorkerMessageType.JoinRoom);
                        bos.WriteGuid(Guid.ParseExact(settings.DebugRoomGuid, "N"));
                    });
                    w.Send(sendData);
                }));
                yield break;
            }
            if (settings.AutomaticRoomLogic)
                InternalJoinAnyRoom();
            ips.Reverse();
        }
        
        bool connected = false;
        public static Dictionary<Guid, KeyPairEntityCollector> keyPairEntityCollectors = new Dictionary<Guid, KeyPairEntityCollector>();
        public static Dictionary<Guid, Dictionary<ulong, NetsEntity>> entityIdToNetsEntity = new Dictionary<Guid, Dictionary<ulong, NetsEntity>>();

        bool initializedSingletons = false;
        bool recievedFirstPacket = false;
        IEnumerator HandlePacketWithDelay(byte[] data) {
            if (DebugLatency > 0) yield return new WaitForSeconds(DebugLatency/1000f);
            HandlePacket(data);
        }

        void HandlePacket(byte[] data) {
            var bb = new BitBuffer(data);
            var category = bb.getByte();
            //if (category != (byte)ProxyToUnityMessageType.Ping)
            //print($"Got category: {category}");

            if (category == (byte)WorkerToClientMessageType.Ping) {
                Guid requestId = bb.ReadGuid();
                SendPong(requestId);
            } else if (category == (byte)WorkerToClientMessageType.JoinedRoom) {
                var roomGuid = bb.ReadGuid();
                myAccountGuid = bb.ReadGuid();
                currentRoom = roomGuid;
                keyPairEntityCollectors[roomGuid] = new KeyPairEntityCollector();
                entityIdToNetsEntity[roomGuid] = new Dictionary<ulong, NetsEntity>();
                keyPairEntityCollectors[roomGuid].AfterEntityCreated = (entity) => {
                    try {
                        //print($"room: {roomGuid:N} created entity {entity.Id}: {entity.PrefabName}");
                        if (entity.Fields.ContainsKey(CreationGuidFieldName)) {

                            var guid = Guid.ParseExact(entity.GetString(CreationGuidFieldName), "N");
                            NetsEntity.NetsEntityByCreationGuidMap.TryGetValue(guid, out var matchedEntity);
                            if (matchedEntity != null) {
                                entityIdToNetsEntity[roomGuid].Add(entity.Id, matchedEntity);
                                matchedEntity.OnCreatedOnServer(roomGuid, entity);
                                return Task.CompletedTask;
                            }
                        }

                        NetworkedTypesLookup.TryGetValue(entity.PrefabName, out var typeToCreate);
                        if (typeToCreate == null) {
                            print("Unable to find object " + entity.Id + " " + entity.PrefabName);
                            return Task.CompletedTask;
                        }

                        if (IsServer && KnownServerSingletons.ContainsKey(typeToCreate.name)) {
                            DestroyEntity(entity.Id);
                            throw new Exception($"Did not create new {typeToCreate.name} as we are server and already have one!");
                        }
                        var newGo = Instantiate(typeToCreate.prefab, new Vector3(999999999, 999999999, 99999999), Quaternion.Euler(0,0,0));
                        var component = newGo.GetComponent<NetsEntity>();
                        if (component.Authority == AuthorityEnum.ServerSingleton) KnownServerSingletons[typeToCreate.name] = component;
                        entityIdToNetsEntity[roomGuid].Add(entity.Id, component);
                        component.OnCreatedOnServer(roomGuid, entity);
                    } catch (Exception e) {
                        Debug.LogError(e);
                    }
                    return Task.CompletedTask;
                };
                keyPairEntityCollectors[roomGuid].AfterEntityUpdated = async (entity) => {
                    try {
                        //print("AfterEntityUpdated");
                        //print($"room: {roomGuid:N} updated entity {entity.Id}");
                        await Task.CompletedTask;
                    } catch (Exception e) {
                        Debug.LogError(e);
                    }
                };
                keyPairEntityCollectors[roomGuid].AfterKeyChanged = async (entity, field) => {
                    try {
                        //print($"room: {roomGuid:N} Updated {entity.Id}.{entity.PrefabName}: [{field.Name}] => {field.Value}");
                        if (entity.Id == 1) {
                            if (entity.PrefabName == "Room") {
                                PlayerCount?.Invoke(entity.GetInt("PlayerCount"));
                                print("ServerAccount: " + entity.GetString("ServerAccount"));
                                if (entity.GetString("ServerAccount")?.Length == 32) {
                                    IsServer = myAccountGuid == Guid.ParseExact(entity.GetString("ServerAccount"), "N");
                                    OnIsServer?.Invoke(IsServer);
                                }
                                if (recievedFirstPacket == false) {
                                    if (IsServer == false) {
                                        var startingEnts = FindObjectsOfType<NetsEntity>();
                                        var localServerEntities = startingEnts
                                            .Where(e => e.Authority.IsServerOwned())
                                            .ToList();
                                        print($"Found {localServerEntities.Count} server entities to destroy as we are not server");
                                        localServerEntities.ForEach(e => {
                                            var comp = e.GetComponent<NetsEntity>();
                                            comp.MarkAsDestroyedByServer(); // Avoid throwing
                                            KnownServerSingletons.Remove(comp.prefab);
                                            Destroy(e.gameObject);
                                        });
                                    }
                                    recievedFirstPacket = true;
                                }

                            } else {
                                throw new Exception("Expected room as entity ID 1");
                            }
                            return;
                        }

                        if (entityIdToNetsEntity.TryGetValue(roomGuid, out var roomDict)) {
                            if (roomDict.TryGetValue(entity.Id, out var e)) {
                                e.OnFieldChange(entity, field.Name);
                            } else {
                                print("Unknown id: " + entity.Id);
                            }
                        } else {
                            print("Unknown room: " + roomGuid);
                        }
                        await Task.CompletedTask;
                    } catch (Exception e) {
                        Debug.LogError(e);
                    }
                };
                keyPairEntityCollectors[roomGuid].AfterEntityRemoved = async (entity) => {
                    try {
                        //print($"Removed {entity.Id}.{entity.PrefabName}");
                        if (entityIdToNetsEntity[roomGuid].TryGetValue(entity.Id, out var e) && e != null && e.gameObject != null) {
                            e.MarkAsDestroyedByServer();
                            Destroy(e.gameObject);
                        }
                        entityIdToNetsEntity[roomGuid].Remove(entity.Id);
                        await Task.CompletedTask;
                    } catch (Exception e) {
                        Debug.LogError(e);
                    }
                };
                OnJoinedRoom?.Invoke(roomGuid);
                RoomsJoined.Add(roomGuid);
            } else if (category == (byte)WorkerToClientMessageType.KeyPairEntityEvent) {
                var roomGuid = bb.ReadGuid();
                //print($"Got entity change for room {roomGuid:N}");
                keyPairEntityCollectors[roomGuid].ApplyDelta(bb, false);
                if (initializedSingletons == false) {
                    if (IsServer) {
                        foreach (var s in typedLists.ServerSingletonsList)
                            if (KnownServerSingletons.ContainsKey(s.name) == false)
                                Instantiate(s.prefab);
                        initializedSingletons = true;
                    }
                }
            } else if (category == (byte)WorkerToClientMessageType.RoomEvent) {
                var roomGuid = bb.ReadGuid();
                var accountGuid = bb.ReadGuid();
                var eventString = bb.ReadString();
                print($"Got room event room {roomGuid:N}. Account {accountGuid:N}. Event: {eventString:N}");
            } else if (category == (byte)WorkerToClientMessageType.EntityEvent) {
                var roomGuid = bb.ReadGuid();
                var senderAccountGuid = bb.ReadGuid();
                var entityId = bb.ReadUnsignedZeroableFibonacci();
                var eventString = bb.ReadString();
                try {
                    if (entityIdToNetsEntity[roomGuid].TryGetValue(entityId, out var nets)) {
                        if(nets)
                            nets.InterpretMethod(eventString);
                    } else {
                        Debug.LogError($"Nets Entity doesn't exist. Room {roomGuid:N}, Entity{entityId}");
                    }
                } catch (Exception e) {
                    Debug.LogError(e);
                }
            } else if(category == (byte)WorkerToClientMessageType.LeftRoom) {
                var roomGuid = bb.ReadGuid();
                RoomsJoined.Remove(roomGuid);
                OnJoinedLeft?.Invoke(roomGuid);
                var callbacks = leaveRoomCallbacks.Where(o => o.Key.Equals(roomGuid));
                foreach (var left in callbacks) {
                    leaveRoomCallbacks.Remove(left.Key);
                    left.Value?.Invoke(left.Key);
                }
            }
        }

        IEnumerator connect(string url) {
            if (connected) {
                if (settings.DebugConnections)
                    print("Already connected");
                yield break;
            }
            WebSocket conn = new WebSocket(new Uri(url));
            print("Attempting connection to game server on " + url);

            if (w != null || connected) {
                print("Closing websocket");
                conn.Close();
                yield return new WaitForSeconds(0.02f);
            }

            conn = new WebSocket(new Uri(url));

            print("waiting for ready");
            while (conn.isReady == false) {
                yield return new WaitForSeconds(.03f); // Wait for iframe to postback
            }

            print("attempting connect");
            yield return StartCoroutine(conn.Connect());
            if (settings.DebugConnections)
                print("Connected to game server? " + conn.isConnected);
            yield return new WaitForSeconds(.2f);

            bool valid = true;
            if (!conn.isConnected) {
                yield return new WaitForSeconds(.02f);
                valid = false;
                print("Attempting reconnect...");
                if (!connected)
                    StartCoroutine(connect(url));
                yield break;
            }
            if (connected) {
                print("Too late for " + url);
                conn.Close(); // Too late
                yield break;
            }

            w = conn;
            connected = true;
            keyPairEntityCollectors.Clear();
            entityIdToNetsEntity.Clear();
            if (settings.DebugConnections)
                print("Debug: valid: " + valid + " , connected " + connected);


            //listener.OnConnected();

            initializedSingletons = false;
            recievedFirstPacket = false;
            while (valid) {
                if (!w.isConnected) {
                    print("ws error!");
                    valid = false;
                    connected = false;
                    oldConnected = false;
                    OnConnect?.Invoke(false);
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
                try {
                    if (DebugLatency > 0)
                        StartCoroutine(HandlePacketWithDelay(data));
                    else
                        HandlePacket(data);
                } catch (Exception e) {
                    OnConnect?.Invoke(false);
                    Debug.LogError(e);
                }

            }
            OnConnect?.Invoke(false);
            w.Close();
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
        public static void LeaveRoom(Guid RoomGuid, Action<Guid> CallBack = null) {
            instance.InternalLeaveRoom(RoomGuid, CallBack);
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
        public static bool LeaveOnlyRoom(Action<Guid> CallBack = null) {
            if (RoomsJoined.Count != 1) return false;
            instance.InternalLeaveRoom(RoomsJoined.First(), CallBack);
            return true;
        }
        /// <summary>
        /// Get all available rooms. This is a Http request so requires a callback when the request is complete.
        /// </summary>
        /// NETS Error on server contact devs
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
        //Internal

        protected bool TryGetObjectFromResponse<T>(UnityWebRequest req, string response, out T obj) {
            obj = default(T);

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
                StartCoroutine(connect($"{(roomState.ip.Contains(":125") ? "wss" : "ws")}://{roomState.ip}"));
                StartCoroutine(WaitUntilConnected(() => {
                    var sendData = BitUtils.ArrayFromStream(bos => {
                        bos.WriteByte((byte)ClientToWorkerMessageType.JoinRoom);
                        bos.WriteGuid(Guid.ParseExact(roomState.token, "N"));
                    });
                    w.Send(sendData);
                }));
                CallBack?.Invoke(roomState);
                JoinRoomResponse?.Invoke(roomState);
            }));
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
        Dictionary<Guid, Action<Guid>> leaveRoomCallbacks = new Dictionary<Guid, Action<Guid>>();
        protected void InternalLeaveRoom(Guid RoomGuid, Action<Guid> CallBack = null) {
            if (leaveRoomCallbacks.ContainsKey(RoomGuid)) return;
            leaveRoomCallbacks.Add(RoomGuid, CallBack);
            var requestGuid = Guid.NewGuid();
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.LeaveRoom);
                bos.WriteGuid(RoomGuid);
                bos.WriteGuid(requestGuid);
            }));
        }

        private IEnumerator SendOnWebRequestComplete(UnityWebRequest webRequest, Action<string> onComplete) {
            webRequest.SendWebRequest();
            while (!webRequest.isDone)
                yield return new WaitForEndOfFrame();
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