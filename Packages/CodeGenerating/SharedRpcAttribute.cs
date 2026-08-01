using System;

namespace RpcGen {
    [AttributeUsage(AttributeTargets.Method)]
    public class SharedRpcAttribute : Attribute {
        public bool RunOnServer { get; set; } = true;
    }
}