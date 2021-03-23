using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using Odessa.Nets.Core;
using Odessa.Nets.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.UnityConverters.Math;
using Odessa.Core;

namespace OdessaEngine.NETS.Core {
    [ExecuteInEditMode]
    public class NetsNetworking : MonoBehaviour {
        //Hooks
        public static Action<Guid> OnJoinedRoom;
        public static Action<Guid> OnLeaveRoom;

        public static Action<Guid, bool> OnConnect;
        public static Action<Guid, bool> OnIsServer;
        public static Action<Guid, int> OnPlayerCountChange;

        private NETSSettings settings => NETSSettings.instance;

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

        public static List<NetsRoomConnection> joinedRooms = new List<NetsRoomConnection>();

        internal static NetsNetworking instance;

        public void Awake() {
            if (!Application.isPlaying) return;
            DontDestroyOnLoad(gameObject);

            if (instance != null) {
                Debug.LogError("Trying to create second Instance of NetsNetworking");
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

		public void Start() {
            if (Application.isPlaying == false) return;

            if (settings.KeepReferenceOfAccount) StartCoroutine(RoomServiceUtils.EnsureAuth());
            if (settings.AutomaticRoomLogic) JoinAnyRoom();
        }

        internal static NetsRoomConnection ConnectViaRoomState(RoomState stateToJoin) {
            print($"Connecting to room state {stateToJoin.token}");
            var connection = new NetsRoomConnection(stateToJoin);

            connection.OnConnect += (connected) => OnConnect?.Invoke(Guid.ParseExact(stateToJoin.token, "N"), connected);
            connection.OnIsServer += (connected) => OnIsServer?.Invoke(Guid.ParseExact(stateToJoin.token, "N"), connected);
            connection.OnPlayerCountChange += (count) => OnPlayerCountChange?.Invoke(Guid.ParseExact(stateToJoin.token, "N"), count);

            joinedRooms.Add(connection);
            return connection;
        }

        private List<NetsEntity> GetAllNetsEntitysOfTypes(AuthorityEnum authType) {
            return FindObjectsOfType<NetsEntity>().Where( e => e.Authority ==  authType).ToList();
        }

        public static void CreateOrJoinRoom(string roomName, Action<NetsRoomConnection> callBack = null, int noPlayerTTL = 30) {
            CreateOrJoinRoom(new RoomConfigData{
                Name = roomName,
                ttlNoPlayers = noPlayerTTL,
            }, callBack);
        }

        public static void CreateOrJoinRoom(RoomConfigData config, Action<NetsRoomConnection> callback = null) =>
            RoomServiceUtils.CreateOrJoinRoom(config, roomState => {
                if (roomState == null) {
                    callback?.Invoke(null);
                    return;
                }
                var conn = ConnectViaRoomState(roomState);
                callback?.Invoke(conn);
            });

        public void JoinAnyRoom(Action<NetsRoomConnection> callback = null) {
            GetAllRooms(available => {
                if (available.Count > 0) {
                    foreach (var av in available) {
                        if (av.playerCount >= 100) {
                            continue;
                        } else {
                            JoinRoom(av.name);
                            return;
                        }
                    }
                    CreateOrJoinRoom(Guid.NewGuid().ToString("N"), callback);
                } else {
                    CreateOrJoinRoom(settings.DefaultRoomName, callback);
                }
            });
        }

        public static void CreateRoom(string roomName, Action<NetsRoomConnection> callback = null, int noPlayerTTL = 30) {
            RoomServiceUtils.CreateRoom(new RoomConfigData {
                Name = roomName,
                ttlNoPlayers = noPlayerTTL,
            }, roomState => {
                var conn = ConnectViaRoomState(roomState);
                callback?.Invoke(conn);
            });
        }

        public static void JoinRoom(string RoomName, Action<NetsRoomConnection> callback = null) {
            RoomServiceUtils.JoinRoom(RoomName, roomState => {
                var conn = ConnectViaRoomState(roomState);
                callback?.Invoke(conn);
            });
        }


        public static List<NetsRoomConnection> GetConnections() => joinedRooms;

        public static void GetAllRooms(Action<List<RoomState>> CallBack) {
            RoomServiceUtils.GetAllRooms(CallBack);
        }

        public static void StartMatchMaking(MatchmakerSettings Settings, Action<MatchMakingResponse> CallBackOnUpdate = null, Action<RoomState> CallBackOnComplete = null) {
            RoomServiceUtils.InternalMatchMakerRequest(Settings, CallBackOnUpdate, CallBackOnComplete);
        }

    }
}