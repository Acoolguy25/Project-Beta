using System.Net.Sockets;
using System.Reflection;
using UnityEngine;

namespace RyanAssets.NetworkService {
    // Unity's Mono reports Socket.OSSupportsIPv6 = true even on hosts where IPv6 is disabled in the
    // kernel (e.g. Linux cloud containers booted with ipv6.disable=1). HttpClient's SocketsHttpHandler
    // then opens a dual-stack IPv6 socket for every request, which fails with "An address
    // incompatible with the requested protocol was used", even for http://127.0.0.1 URLs.
    // When an IPv6 socket can't be created, correct Mono's cached flag so HttpClient connects over IPv4.
    static class IPv6Support {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            if (!Socket.OSSupportsIPv6)
                return;
            try {
                using (new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp)) { }
                return; // IPv6 works.
            } catch (SocketException) { }
            FieldInfo flag = typeof(Socket).GetField("s_OSSupportsIPv6", BindingFlags.NonPublic | BindingFlags.Static);
            if (flag == null)
                return;
            flag.SetValue(null, false);
            Debug.Log("IPv6 is unavailable on this host; network requests will use IPv4.");
        }
    }
}
