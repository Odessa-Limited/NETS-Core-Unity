using Odessa.Nets.EntityTracking;
using UnityEngine;

namespace OdessaEngine.NETS.Core {
    [RequireComponent(typeof(NetsEntity))]
    public class NetworkedBehavior : MonoBehaviour {
        private NetsEntity _netsEntity;
        private NetsEntity netsEntity {
            get {
                if (_netsEntity != null) _netsEntity = transform.GetComponent<NetsEntity>();
                return null;
            }

        }

        public bool IsOwnedByMe => netsEntity.OwnedByMe;

        public virtual void ServerUpdate() { }
        public virtual void SaveState(KeyPairEntity entity) { }
        public virtual void OwnedUpdate() { }
        public virtual void OnFieldUpdate(KeyPairEntity entity, string key) { }
    }
}
