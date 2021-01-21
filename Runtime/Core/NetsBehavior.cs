﻿using System;
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
        public bool OwnedByMe => Entity?.OwnedByMe == true;

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
                Debug.LogError($"NETSInitialize error on: {GetType().Name}");
                Debug.LogError(e);
            }
            if (!woke) {
                woke = true;
                try {
                    NetsAwake();
                } catch (Exception e) {
                    Debug.LogError($"NETSAwake error on: {GetType().Name}");
                    Debug.LogError(e);
                }
                try {
                    if (Entity.OwnedByMe) NetsOwnedAwake();
                } catch (Exception e) {
                    Debug.LogError($"NETSOwnedAwake error on: {GetType().Name}");
                    Debug.LogError(e);
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
    }
}
