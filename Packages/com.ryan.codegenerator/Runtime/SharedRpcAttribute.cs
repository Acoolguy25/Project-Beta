using System;

namespace RpcGen {
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class SharedRpcAttribute : Attribute {
        public bool RunOnServer { get; set; } = true; // Run on server
        public bool RunOnCallingServer { get; set; } = true; // Run On Server if called from server
        public bool RunOnCallingClient { get; set; } = false; // Run On Client if called from client
    }
}
