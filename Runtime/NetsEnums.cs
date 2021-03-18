using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdessaEngine.NETS.Core {
    public enum AuthorityEnum {
        Client,
        Server,
        ServerSingleton,
    }

    public enum NetsEntityState {
        Uninitialized,
        Pending,
        Insync
    }
}