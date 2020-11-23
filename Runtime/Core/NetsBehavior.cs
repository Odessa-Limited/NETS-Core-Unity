using UnityEngine;

namespace OdessaEngine.NETS.Core {
    [RequireComponent(typeof(NetsEntity))]
    public class NetsBehavior : MonoBehaviour {
        private NetsEntity _netsEntity;
        public NetsEntity Entity {
            get {
                if (_netsEntity == null) _netsEntity = transform.GetComponent<NetsEntity>();
                return _netsEntity;
            }

        }

        public virtual void NetsStart() { }
        public virtual void NetsUpdate() { }
    }
}