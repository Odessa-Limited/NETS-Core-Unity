using Odessa.Nets.Core;
using Odessa.Nets.Core.Models;
using Odessa.Nets.EntityTracking;
using Odessa.Nets.EntityTracking.EventObjects.NativeEvents;
using Odessa.Nets.EntityTracking.Wrappers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OdessaEngine.NETS.Core {
    public class NetsRoomConnection {

        public static Action<Guid, int> OnPlayersInRoomLeft;
        public static Action<Guid, int> OnPlayersInRoomJoined;

        public static Action<bool> OnConnect;
        public static Action<bool> OnIsServer;


        public bool IsServer = false;
        public static Guid? myAccountGuid { get; set; } = null;
        public static Guid? roomGuid = null;

        WebSocket w;

        public bool canSend => w?.isConnected == true;


        bool oldConnected = false;

        public static Dictionary<string, NetsEntity> KnownServerSingletons = new Dictionary<string, NetsEntity>();


        private NETSSettings settings => NETSSettings.instance;
        private MonoBehaviour monoRef = NetsNetworking.instance;
        void print(string s) => MonoBehaviour.print(s);
        Coroutine StartCoroutine(IEnumerator e) => monoRef.StartCoroutine(e);

        public NetsRoomConnection(RoomState roomState) {
            var protocol = roomState.ip.Contains(":125") ? "wss" : "ws";
            StartCoroutine(connect($"{protocol}://{settings.DebugWorkerUrlAndPort}"));
            StartCoroutine(WaitUntilConnected(() => {
                var sendData = BitUtils.ArrayFromStream(bos => {
                    bos.WriteByte((byte)ClientToWorkerMessageType.JoinRoom);
                    bos.WriteGuid(Guid.ParseExact(settings.DebugRoomGuid, "N"));
                });
                w.Send(sendData);
            }));

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
            room = new RoomModel();
            entityIdToNetsEntity.Clear();
            StartCoroutine(SendChanges());
            if (settings.DebugConnections) print("Debug: valid: " + valid + " , connected " + connected);

            //listener.OnConnected();
            HasJoinedRoom = false;
            initializedSingletons = false;
            //foreach (var ent in GetAllNetsEntitysOfTypes(AuthorityEnum.ServerSingleton)) {
            //    Destroy(ent.gameObject);
            //}
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
                if (settings.DebugLatency > 0)
                    yield return HandlePacketWithDelay(data);
                else
                    HandlePacket(data);

            }
            OnConnect?.Invoke(false);
            w.Close();
        }

        public static List<EntityModel> Players() {
            return room.ClientConnectionModels.Values.ToList();
        }

        bool intentionallyDisconnected = false;
        private void Disconnect() {
            Debug.Log("On Disconnect from NETS");
            intentionallyDisconnected = true;
            w.Close();

            foreach (var e in entityIdToNetsEntity.Values) {
                e.MarkAsDestroyedByServer(); // Avoid throwing
                KnownServerSingletons.Remove(e.prefab);
                if (e && e.gameObject)
                    MonoBehaviour.Destroy(e.gameObject);
            }

            NetsEntity.NetsEntityByUniqueIdMap.Clear();
            entityIdToNetsEntity.Clear();
            room = new RoomModel();
            KnownServerSingletons.Clear();

            foreach (var e in MonoBehaviour.FindObjectsOfType<NetsEntity>()) {
                e.MarkAsDestroyedByServer(); // Avoid throwing
                MonoBehaviour.Destroy(e.gameObject);
            }
            LeaveRoom();
        }


        bool connected = false;
        public static RoomModel room;
        public static Dictionary<string, NetsEntity> entityIdToNetsEntity = new Dictionary<string, NetsEntity>();

        bool initializedSingletons = false;
        bool HasJoinedRoom = false;
        IEnumerator HandlePacketWithDelay(byte[] data) {
            if (settings.DebugLatency > 0)
                yield return new WaitForSeconds(settings.DebugLatency / 1000f);
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
                    roomGuid = bb.ReadGuid();
                    if (settings.DebugConnections)
                        Debug.Log($"NETS - Joined Room ID {roomGuid}");
                    myAccountGuid = bb.ReadGuid();
                    room = new RoomModel();

                    keyPairEntityCollectors[roomGuid].AfterEntityCreated = (entity) => {
                        try {
                            if (settings.DebugConnections) print($"Created entity {entity.Id}: {entity.PrefabName}");
                            if (entity.Fields.ContainsKey(AssignedGuidFieldName)) {
                                var guid = Guid.ParseExact(entity.GetString(AssignedGuidFieldName), "N");
                                NetsEntity.NetsEntityByUniqueIdMap.TryGetValue(guid, out var matchedEntity);
                                if (matchedEntity != null) {
                                    entityIdToNetsEntity[roomGuid].Add(entity.Id, matchedEntity);
                                    matchedEntity.OnCreatedOnServer(roomGuid, entity);
                                    matchedEntity.Owner = entity.Owner;
                                    return Task.CompletedTask;
                                }
                            }

                            if (entity.PrefabName == "?ClientConnection") {
                                OnPlayersInRoomJoined?.Invoke(Guid.ParseExact(entity.GetString("AccountGuid"), "N"), Players().Count);
                            }
                            NetworkedTypesLookup.TryGetValue(entity.PrefabName, out var typeToCreate);
                            if (typeToCreate == null) {
                                if (entity.PrefabName.Contains("?") == false)
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
                                OnPlayersInRoomLeft?.Invoke(Guid.ParseExact(entity.GetString("AccountGuid"), "N"), Players().Count);
                            }
                            await Task.CompletedTask;
                        } catch (Exception e) {
                            Debug.LogError(e);
                        }
                    };

                } else if (category == (byte)WorkerToClientMessageType.EntityEvent) {
                    var roomGuid = bb.ReadGuid();
                    //print($"Got entity change for room {roomGuid:N}");
                    try {
                        room.ApplyRootEvent(NativeEventUtils.DeserializeNativeEventType(bb));
                        // After apply events

                    } catch (Exception e) {
                        Debug.LogError(e);
                    }
                } else if (category == (byte)WorkerToClientMessageType.RoomEvent) {
                    var roomGuid = bb.ReadGuid();
                    var accountGuid = bb.ReadGuid();
                    var eventString = bb.ReadString();
                } else if (category == (byte)WorkerToClientMessageType.EntityRpc) {
                    var senderAccountGuid = bb.ReadGuid();
                    var entityId = bb.ReadMaybeLimitedString();
                    var eventString = bb.ReadString();
                    try {
                        entityIdToNetsEntity.TryGetValue(entityId, out var entity);
                        if (entity == null) {
                            Debug.LogError($"Nets Entity doesn't exist. Room {roomGuid:N}, Entity{entityId}");
                            return;
                        }
                        entityIdToNetsEntity[entityId].InterpretMethod(eventString);
                    } catch (Exception e) {
                        Debug.LogError(e);
                    }
                }
            } catch (Exception e) {
                OnConnect?.Invoke(false);
                Debug.LogError(e);
            }
        }

        private IEnumerator DelayPing(Guid requestId) {
            yield return new WaitForSeconds(1);
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.Pong);
                bos.WriteGuid(requestId);
            }));
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

        public void SendRoomEvent(Guid roomGuid, string eventString = "") {
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.RoomEvent);
                bos.WriteString(eventString);
            }));
        }
        public void SendEntityEvent(NetsEntity entity, string eventString = "") {
            w.Send(BitUtils.ArrayFromStream(bos => {
                bos.WriteByte((byte)ClientToWorkerMessageType.EntityEvent);
                bos.WriteMaybeLimitedString(entity.Model().uniqueId);
                bos.WriteString(eventString);
            }));
        }

        float sendDelay = 0.2f;
        IEnumerator SendChanges() {
            while (w.isConnected) {
                yield return new WaitForSeconds(sendDelay);

                var changes = room.FlushEvents();
                if (changes == null) continue;

                w.Send(BitUtils.ArrayFromStream(bos => {
                    bos.WriteByte((byte)ClientToWorkerMessageType.EntityEvent);
                    changes.Serialize(bos, EntitySerializationContext.Admin);
                }));
            }
        }

        private IEnumerator WaitUntilConnected(Action action) {
            while (!connected)
                yield return new WaitForEndOfFrame();
            action?.Invoke();
        }

        public void LeaveRoom() {
            var requestGuid = Guid.NewGuid();
            try {
                w.Send(BitUtils.ArrayFromStream(bos => {
                    bos.WriteByte((byte)ClientToWorkerMessageType.LeaveRoom);
                    bos.WriteGuid(requestGuid);
                }));
            } catch (Exception e) {
                Debug.LogError(e);
            }
            Disconnect();
        }





    }
}