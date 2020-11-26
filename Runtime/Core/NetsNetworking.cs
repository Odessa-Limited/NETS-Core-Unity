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
using Newtonsoft.Json.UnityConverters.AI.NavMesh;
using Newtonsoft.Json.UnityConverters.Geometry;
using Newtonsoft.Json.UnityConverters.Hashing;
using Newtonsoft.Json.UnityConverters.Physics;
using Newtonsoft.Json.UnityConverters.Physics2D;
using Newtonsoft.Json.UnityConverters.Scripting;
using static OdessaEngine.NETS.Core.NetsEntity;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace OdessaEngine.NETS.Core {
    [ExecuteInEditMode]
    public class NetsNetworking : MonoBehaviour {

        int RoomServicePort = 8001;
        string URL = "roomservice.nets.odessaengine.com";
        public string applicationGuid = "0123456789abcdef0123456789abcdef";
        public bool UseLocal = false;
        public string DefaultRoomName = "default2";
        [Range(0, 500)]
        public float DebugLatency = 0f;
        [Header("Developer Debug")]
        public bool hitWorkerDirectly = false;
        public string DebugWorkerUrlAndPort = "140.82.41.234:12334";
        public string DebugRoomGuid = "00000000000000000000000000000000";
        bool debugConnections = true;



        static NetsNetworking() {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> {
                    new Vector2Converter(),
                    new Vector2IntConverter(),
                    new Vector3Converter(),
                    new Vector3IntConverter(),
                    new Vector4Converter(),
                    new NavMeshQueryFilterConverter(),
                    new NavMeshTriangulationConverter(),
                    new BoundsConverter(),
                    new BoundsIntConverter(),
                    new PlaneConverter(),
                    new RectConverter(),
                    new RectIntConverter(),
                    new RectOffsetConverter(),
                    new Hash128Converter(),
                    new ColorConverter(),
                    new Color32Converter(),
                    new QuaternionConverter(),
                    new SphericalHarmonicsL2Converter(),
                    new Vector2Converter(),
                    new Vector2IntConverter(),
                    new Vector3Converter(),
                    new Vector3IntConverter(),
                    new Vector4Converter(),
                    new JointDriveConverter(),
                    new JointLimitsConverter(),
                    new SoftJointLimitConverter(),
                    new ContactFilter2DConverter(),
                    new LayerMaskConverter(),
                    new RangeIntConverter(),
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
        bool usingSecure = false;

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
                bos.WriteByte((byte)WorkerToClientMessageType.Ping);
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
        }

		// Use this for initialization
		public IEnumerator Start() {
            if (!Application.isPlaying) yield break;

            if (UseLocal) URL = "127.0.0.1";
            instance = this;
            //GameInstance.placeholderEntities = showPlaceholderPrefabs;

            // Get IP list and connect to them all ( try both http and https, we don't know what we are using )
            print("Getting servers");

#if UNITY_WEBGL
        if (Application.absoluteURL.StartsWith("https://")) usingSecure = true;
#endif
            //ips.Add("ws://127.0.0.1:" + port);
            //ips.Add("wss://" + URL + ":" + (port + 1000));
            if (hitWorkerDirectly) {

                StartCoroutine(connect($"{(DebugWorkerUrlAndPort.Contains(":125") ? "wss" : "ws")}://{DebugWorkerUrlAndPort}"));
                StartCoroutine(WaitUntilConnected(() => {
                    var sendData = BitUtils.ArrayFromStream(bos => {
                        bos.WriteByte((byte)ClientToWorkerMessageType.JoinRoom);
                        bos.WriteGuid(Guid.ParseExact(DebugRoomGuid, "N"));
                    });
                    w.Send(sendData);
                }));
                yield break;
            }
            if (!usingSecure)
                URL = $"{URL}:{RoomServicePort}";
            CreateOrJoinRoom(URL, DefaultRoomName);
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

                        var newGo = Instantiate(typeToCreate.prefab);
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
                                print("PlayerCount: " + entity.GetInt("PlayerCount"));
                                print("ServerAccount: " + entity.GetString("ServerAccount"));
                                if (entity.GetString("ServerAccount")?.Length == 32) {
                                    IsServer = myAccountGuid == Guid.ParseExact(entity.GetString("ServerAccount"), "N");
                                    OnIsServer?.Invoke(IsServer);
                                }
                                if (recievedFirstPacket == false) {
                                    if (IsServer == false) {
                                        var localServerEntities = FindObjectsOfType<NetsEntity>()
                                            .Where(e => e.Authority.IsServerOwned())
                                            .ToList();
                                        print($"Found {localServerEntities.Count} server entities to destroy as we are not server");
                                        localServerEntities.ForEach(e => {
                                            var comp = e.GetComponent<NetsEntity>();
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
                        if (entityIdToNetsEntity[roomGuid].TryGetValue(entity.Id, out var e) && e != null && e.gameObject != null) Destroy(e.gameObject);
                        entityIdToNetsEntity[roomGuid].Remove(entity.Id);
                        await Task.CompletedTask;
                    } catch (Exception e) {
                        Debug.LogError(e);
                    }
                };

            } else if (category == (byte)WorkerToClientMessageType.KeyPairEntityEvent) {
                var roomGuid = bb.ReadGuid();
                //print($"Got entity change for room {roomGuid:N}");
                keyPairEntityCollectors[roomGuid].ApplyDelta(bb, false);
                if (initializedSingletons == false) {
                    if (IsServer) {
                        foreach (var s in ServerSingletonsList)
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
                        nets.InterpretMethod(eventString);
                    } else {
                        Debug.LogError($"Nets Entity doesn't exist. Room {roomGuid:N}, Entity{entityId}");
                    }
                } catch (Exception e) {
                    Debug.LogError(e);
                }
            }
        }

        IEnumerator connect(string url) {
            if (connected) {
                if (debugConnections)
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
            print("attempting connect");
            yield return StartCoroutine(conn.Connect());
            if (debugConnections)
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

            print("Connected to " + url);
            w = conn;
            connected = true;
            keyPairEntityCollectors.Clear();
            entityIdToNetsEntity.Clear();
            if (debugConnections)
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
                if (_networkedTypesLookup == null) _networkedTypesLookup = NetworkedTypesList.ToDictionary(t => t.name, t => t);
                return _networkedTypesLookup;
            }
        }

        [HideInInspector]
        public List<NetworkObjectConfig> NetworkedTypesList = new List<NetworkObjectConfig>();
        [HideInInspector]
        public List<NetworkObjectConfig> ServerSingletonsList = new List<NetworkObjectConfig>();

#if UNITY_EDITOR
        void Update() {
            if (Application.isPlaying) return;
            NetworkedTypesList.Clear();
            ServerSingletonsList.Clear();

            var allPaths = AssetDatabase.GetAllAssetPaths();
            foreach (var path in allPaths) {
                var loaded = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if(!loaded) 
                    continue;
                if (loaded.GetType() != typeof(GameObject)) continue;
                var asGo = loaded as GameObject;

                var networkedComponentList = asGo.GetComponents<NetsEntity>().ToList();
                if (networkedComponentList.Count == 0) continue;
                if (networkedComponentList.Count > 1) {
                    Console.Error.WriteLine("Entity " + path + " has two NetsEntity components");
                    continue;
                }
                var networkedComponent = networkedComponentList.Single();
                //if (networkedComponent.GetType().Name != asGo.name) throw new Exception("Name mismatch - Gameobject " + asGo.name + " has networked class " + networkedComponent.GetType().Name);
                if (NetworkedTypesList.Any(n => n.name == networkedComponent.GetType().Name)) continue;
                networkedComponent.prefab = asGo.name;
                NetworkedTypesList.Add(new NetworkObjectConfig {
                    name = asGo.name,
                    prefab = asGo,
                });
                if (networkedComponent.Authority == AuthorityEnum.ServerSingleton) {
                    ServerSingletonsList.Add(new NetworkObjectConfig {
                        name = asGo.name,
                        prefab = asGo,
                    });
                }

            }
        }
#endif
        public void CreateOrJoinRoom(string url, string roomName) {
            string requestMethod = usingSecure ? "https" : "http";
            var webRequest = UnityWebRequest.Get($"{requestMethod}://{url}/joinOrCreateRoom?token={applicationGuid}&roomConfig={JsonUtility.ToJson(new RoomConfigData() { Name = roomName, ttlNoPlayers = 30 })}");
            StartCoroutine(SendOnWebRequestComplete(webRequest, (resultText) => {
                if (string.IsNullOrEmpty(resultText) || resultText.ToLower().Contains("exception")) {
                    //This should probably send a notification to our channels via webhook
                    Debug.LogError("NETS Error on server contact devs");
                    return;
                }
                RoomState roomState=null;
                try {
                    roomState = JsonUtility.FromJson<RoomState>(resultText);
                }catch( Exception e) {
                    Debug.LogError("NETS Error on server contact devs");
                    return;
                }
                StartCoroutine(connect($"{(roomState.ip.Contains(":125") ? "wss" : "ws")}://{roomState.ip}"));
                StartCoroutine(WaitUntilConnected(() => {
                    var sendData = BitUtils.ArrayFromStream(bos => {
                        bos.WriteByte((byte)ClientToWorkerMessageType.JoinRoom);
                        bos.WriteGuid(Guid.ParseExact(roomState.token, "N"));
                    });
                    w.Send(sendData);
                }));
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