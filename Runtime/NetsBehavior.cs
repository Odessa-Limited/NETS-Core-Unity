using System;
using System.Reflection;
using UnityEngine;

namespace OdessaEngine.NETS.Core {
    public abstract class NetsBehavior : MonoBehaviour {
        private NetsEntity _netsEntity;
        public NetsEntity Entity {
            get {
                if (_netsEntity == null) _netsEntity = transform.GetComponent<NetsEntity>();
                if (_netsEntity == null) throw new Exception($"NETS Error: No NetsEntity script attached to object with script {GetType().Name}");
                return _netsEntity;
            }

        }
        public void RPC(Action method) => Entity.RPC(method);
        public void RPC<T1>(Action<T1> method, T1 arg1) => Entity.RPC(method, arg1);
        public void RPC<T1, T2>(Action<T1, T2> method, T1 arg1, T2 arg2) => Entity.RPC(method, arg1, arg2 );
        public void RPC<T1, T2, T3>(Action<T1, T2, T3> method, T1 arg1, T2 arg2, T3 arg3) => Entity.RPC(method, arg1, arg2, arg3 );
        public void RPC<T1, T2, T3, T4>(Action<T1, T2, T3, T4> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => Entity.RPC(method, arg1, arg2, arg3, arg4 );
        public void RPC<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => Entity.RPC(method,  arg1, arg2, arg3, arg4, arg5 );
        public void RPC<T1, T2, T3, T4, T5, T6>(Action<T1, T2, T3, T4, T5, T6> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) => Entity.RPC(method,  arg1, arg2, arg3, arg4, arg5, arg6 );
        public void RPC<T1, T2, T3, T4, T5, T6, T7>(Action<T1, T2, T3, T4, T5, T6, T7> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) => Entity.RPC(method, arg1, arg2, arg3, arg4, arg5, arg6, arg7 );
        public void RPC<T1, T2, T3, T4, T5, T6, T7, T8>(Action<T1, T2, T3, T4, T5, T6, T7, T8> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) => Entity.RPC(method, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 );
        public void RPC<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9) => Entity.RPC(method, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9 );
        public void RPC<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> method, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10) => Entity.RPC(method, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10 );
        public void RPC(Action method, object[] parameters) => Entity.RPC(method, parameters);
        public bool IsOwnedByMe => Entity?.IsOwnedByMe == true;

        private bool initialized = false;
        private bool woke = false;

        public void TryInitialize() {
            if (initialized) return;
            NetsInitialize();
            initialized = true;
        }

        public void Awake() {
            try {
                TryInitialize();
            } catch (Exception e) {
                Debug.LogError($"NETSInitialize error on: {GetType().Name}\n{e}");
            }
            if (!woke) {
                woke = true;
                try {
                    NetsAwake();
                } catch (Exception e) {
                    Debug.LogError($"NETSAwake error on: {GetType().Name}\n{e}");
                }
                try {
                    if (Entity.IsOwnedByMe) NetsOwnedAwake();
                } catch (Exception e) {
                    Debug.LogError($"NETSOwnedAwake error on: {GetType().Name}\n{e}");
                }
            }
        }

        public abstract void NetsInitialize();
        public abstract void NetsAwake();
        public abstract void NetsStart();
        public abstract void NetsUpdate();
        public virtual void NetsOwnedAwake() { }
        public virtual void NetsOwnedStart() { }
        public virtual void NetsOwnedUpdate() { }
        public virtual void NetsOnGainOwnership() { }
        public virtual void NetsOnLostOwnership() { }
    }
}
