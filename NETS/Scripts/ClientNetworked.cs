using UnityEngine;

[RequireComponent(typeof(NetsEntity))]
public abstract class ClientNetworked : MonoBehaviour {
    private NetsEntity _netsEntity;
    public NetsEntity netsEntity { get { 
            if(_netsEntity == null) {
                _netsEntity = GetComponent<NetsEntity>();
            }
            return _netsEntity;
        } }
    public virtual void OwnedUpdate() { }
}
