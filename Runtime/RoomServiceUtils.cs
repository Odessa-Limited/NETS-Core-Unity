using Newtonsoft.Json;
using Odessa.Core;
using Odessa.Core.Models.Enums;
using Odessa.Nets.Core.Models;
using OdessaEngine.NETS.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class RoomServiceUtils : MonoBehaviour {
    public static MatchMakingResponse CurrentMatchMaking { get; set; } = null;
    
    private const string NETS_AUTH_TOKEN = "NETS_AUTH_TOKEN";
    private static NETSSettings settings => NETSSettings.instance;


#if DEVELOPMENT_BUILD || UNITY_EDITOR
    static string roomserviceUrl => settings.UseLocalServices ? "http://127.0.0.1:8001" : NetsNetworkingConsts.NETS_ROOM_SERVICE_URL;
    static string authUrl => settings.UseLocalServices ? "http://127.0.0.1:8002" : NetsNetworkingConsts.NETS_AUTH_SERVICE_URL;
#else
    static string url => NetsNetworkingConsts.NETS_ROOM_SERVICE_URL;
    static string authUrl => NetsNetworkingConsts.NETS_AUTH_SERVICE_URL;
#endif

    private static AuthResponse currentAuth;
    private static long refreshTokenAt = -1;
    private static string authSubject = null;
    private static bool gettingAuth = false;

    public static Guid GetMyAccountGuid() => authSubject != null ? Guid.ParseExact(authSubject, "N") : default;
    public static string GetMyAccountJWT() => currentAuth.accessToken;

    internal static IEnumerator EnsureAuth() {
        var needsToken = currentAuth == null;
        var needsRefresh = DateTimeOffset.Now.ToUnixTimeSeconds() > refreshTokenAt;
        if ((needsToken || needsRefresh) == false) yield break;

        if (needsToken || needsRefresh) {
            while (gettingAuth) yield return new WaitForSecondsRealtime(0.1f);

            needsToken = currentAuth == null;
            needsRefresh = DateTimeOffset.Now.ToUnixTimeSeconds() > refreshTokenAt;
            if ((needsToken || needsRefresh) == false) yield break;

            gettingAuth = true;

            if (!settings.KeepReferenceOfAccount) {
                PlayerPrefs.SetString(NETS_AUTH_TOKEN, default);
            }

            if (needsToken) {
                var cache = PlayerPrefs.GetString(NETS_AUTH_TOKEN, default);
                if (cache != default && cache.Length > 0) {
                    currentAuth = JsonConvert.DeserializeObject<AuthResponse>(PlayerPrefs.GetString(NETS_AUTH_TOKEN, default));
                } else {
                    var webRequest = UnityWebRequest.Get($"{authUrl}/createAnonUser?applicationGuid={settings.ApplicationGuid}");
                    yield return webRequest.SendWebRequest();
                    while (webRequest.isDone == false) yield return new WaitForSecondsRealtime(0.05f); // 50ms
                    HandleAuthResponse(webRequest, webRequest.downloadHandler.text);
                    needsRefresh = false;
                }
            }

            if (needsRefresh) {
                var webRequest = UnityWebRequest.Get($"{authUrl}/refresh?applicationGuid={settings.ApplicationGuid}&refreshToken={currentAuth.refreshToken}");
                yield return webRequest.SendWebRequest();

                if (HandleAuthResponse(webRequest, webRequest.downloadHandler.text) == false) {
                    currentAuth = null;
                    PlayerPrefs.DeleteKey(NETS_AUTH_TOKEN);
                    EnsureAuth();
                }
            }

            gettingAuth = false;
        }
    }
    internal static void EnsureAuthSync() {
        var authFunc = EnsureAuth();
        while (authFunc.MoveNext());
    }

    protected static bool TryGetObjectFromResponse<T>(UnityWebRequest req, string response, out T obj) {
        obj = default;
        if (req.responseCode == 401) {
            Debug.LogError($"NETS Error - unauthorized. Code: {req.responseCode} Error: {response}");
            refreshTokenAt = -1;
            return false;
        }
        if (req.responseCode != 200) {
            //This should probably send a notification to our channels via webhook
            Debug.LogError($"NETS Error on server, contact devs. Code: {req.responseCode} Error: {response}");
            return false;
        }

        try {
            obj = JsonConvert.DeserializeObject<T>(response);
            return true;
        } catch (Exception e) {
            Debug.LogError($"NETS format error on server, contact devs, Error: {e} Response: {response}");
            return false;
        }
    }

    internal static IEnumerator InternalMatchMakerRequest(MatchmakerSettings settings, Action<MatchMakingResponse> CallBackOnUpdate = null, Action<RoomState> CallBackOnComplete = null) {
        Dictionary<string, int> regionalPings = new Dictionary<string, int>();
        regionalPings.Add("USE", 0);
        var matchMakingState = MatchMakingState.IN_QUEUE;
        MatchMakingResponse result = default;
        while (matchMakingState != MatchMakingState.IN_GAME) {
            var requestComplete = false;
            
            yield return EnsureAuth();
            var toUseUrl = $"{roomserviceUrl}/matchMakerRequest?accountToken={currentAuth.accessToken}&settings={JsonConvert.SerializeObject(settings)}&pings={JsonConvert.SerializeObject(regionalPings)}";
            var webRequest = UnityWebRequest.Get(toUseUrl);
            var coroutine = NetsNetworking.instance.StartCoroutine(SendOnWebRequestComplete(webRequest, (resultText) => {
                if (!TryGetObjectFromResponse(webRequest, resultText, out MatchMakingResponse matchMakingResponse)) {
                    requestComplete = true;
                    return;
                }
                matchMakingState = matchMakingResponse.State;
                CallBackOnUpdate?.Invoke(matchMakingResponse);
                CurrentMatchMaking = matchMakingResponse;
                result = matchMakingResponse;
                requestComplete = true;
            }));
            while (!requestComplete) yield return new WaitForSecondsRealtime(0.05f);
            yield return new WaitForSecondsRealtime(2);
        }
        CallBackOnComplete(result?.RoomState);
    }

    private static bool HandleAuthResponse(UnityWebRequest webRequest, string resultText) {
        if (!TryGetObjectFromResponse(webRequest, resultText, out AuthResponse authResponse)) return false;
        currentAuth = authResponse;

        if (settings.KeepReferenceOfAccount) {
            PlayerPrefs.SetString(NETS_AUTH_TOKEN, JsonConvert.SerializeObject(authResponse));
        } else {
            PlayerPrefs.SetString(NETS_AUTH_TOKEN, default);
        }

        var tokenInfo = DecodeJwt(authResponse.accessToken);
        var tokenLifetime = Convert.ToInt64(tokenInfo["exp"]) - Convert.ToInt64(tokenInfo["iat"]);
        refreshTokenAt = DateTimeOffset.Now.ToUnixTimeSeconds() + tokenLifetime / 2;
        authSubject = tokenInfo["sub"];
        return true;
    }

    private static Dictionary<string, string> DecodeJwt(string jwt) {
        var parts = jwt.Split('.');
        if (parts.Length != 3) throw new Exception("Not a JWT");
        return JsonConvert.DeserializeObject<Dictionary<string, string>>(Encoding.ASCII.GetString(Convert.FromBase64String(parts[1])));

    }

    internal static void CreateRoom(RoomConfigData config, Action<RoomState> CallBack = null) {
        var webRequest = UnityWebRequest.Get($"{roomserviceUrl}/createRoom?token={settings.ApplicationGuid}&roomConfig={JsonUtility.ToJson(config)}");
        NetsNetworking.instance.StartCoroutine(SendOnWebRequestComplete(webRequest, (resultText) => {
            if (!TryGetObjectFromResponse(webRequest, resultText, out RoomState roomState)) return;
            CallBack?.Invoke(roomState);
        }));
    }

    internal static void JoinRoom(string RoomName, Action<RoomState> CallBack = null) {
        var webRequest = UnityWebRequest.Get($"{roomserviceUrl}/joinRoom?token={settings.ApplicationGuid}&roomName={RoomName}");
        NetsNetworking.instance.StartCoroutine(SendOnWebRequestComplete(webRequest, (resultText) => {
            if (!TryGetObjectFromResponse(webRequest, resultText, out RoomState roomState)) {
                CallBack(null);
                return;
            }
            CallBack(roomState);
        }));
    }

    internal static void GetAllRooms(Action<List<RoomState>> CallBack) {
        EnsureAuthSync();

        var webRequest = UnityWebRequest.Get($"{roomserviceUrl}/listRooms?token={settings.ApplicationGuid}");
        NetsNetworking.instance.StartCoroutine(SendOnWebRequestComplete(webRequest, (resultText) => {
            if (!TryGetObjectFromResponse(webRequest, resultText, out List<RoomState> roomStates)) {
                CallBack(null);
                return;
            }
            CallBack?.Invoke(roomStates);
        }));
    }

    internal static void CreateOrJoinRoom(RoomConfigData config, Action<RoomState> CallBack = null) {
        var webRequest = UnityWebRequest.Get($"{roomserviceUrl}/joinOrCreateRoom?token={settings.ApplicationGuid}&roomConfig={JsonUtility.ToJson(config)}");
        NetsNetworking.instance.StartCoroutine(SendOnWebRequestComplete(webRequest, (resultText) => {
            TryGetObjectFromResponse(webRequest, resultText, out RoomState roomState);
            CallBack?.Invoke(roomState);
        }));
    }

    private static IEnumerator SendOnWebRequestComplete(UnityWebRequest webRequest, Action<string> onComplete) {
        yield return webRequest.SendWebRequest();
        //TODO handle errors
        onComplete?.Invoke(webRequest.downloadHandler.text);
    }

}
