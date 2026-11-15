using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using MonoFN.Cecil;
using MonoFN.Cecil.Cil;

namespace FishNet.CodeGenerating.Processing.Rpc {
    /// <summary>
    /// Expands RpcGen.SharedRpcAttribute into native FishNet RPC entry points
    /// before FishNet's normal RPC processor runs.
    /// </summary>
    internal static class SharedRpcProcessor {
        private const string ATTRIBUTE_NAME = "RpcGen.SharedRpcAttribute";

        // isServerAssembly is no longer used (the dispatch branch is now decided at
        // runtime via NetworkBehaviour.IsServer instead of at weave time), but the
        // parameter is kept so any existing call site doesn't need to change.
        internal static bool Process(ModuleDefinition module, bool isServerAssembly = false) {
            bool modified = false;
            foreach (TypeDefinition type in GetAllTypes(module.Types).ToArray()) {
                foreach (MethodDefinition method in type.Methods.ToArray()) {
                    CustomAttribute attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == ATTRIBUTE_NAME);
                    if (attribute == null || !CanTransform(method))
                        continue;

                    Transform(module, type, method, attribute);
                    modified = true;
                }
            }
            return modified;
        }

        private static bool CanTransform(MethodDefinition methodDef) {
            return methodDef.HasBody && !methodDef.IsStatic && !methodDef.HasGenericParameters &&
                   methodDef.ReturnType.MetadataType == MetadataType.Void &&
                   methodDef.Parameters.All(p => !p.ParameterType.IsByReference);
        }

        private static void Transform(ModuleDefinition module, TypeDefinition typeDef, MethodDefinition logicMethod, CustomAttribute attribute) {
            string methodName = logicMethod.Name;

            bool runOnServer = ReadBool(attribute, "RunOnServer", true);
            bool runOnCallingServer = ReadBool(attribute, "RunOnCallingServer", true);
            bool runOnCallingClient = ReadBool(attribute, "RunOnCallingClient", false);

            // Keep the authored body as a private implementation method, then use
            // the original public name as the dispatch method all existing callers use.
            logicMethod.Name = $"__SharedRpcLogic_{methodName}_{logicMethod.MetadataToken.RID}";
            logicMethod.CustomAttributes.Remove(attribute);
            logicMethod.Attributes = (logicMethod.Attributes & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Private;

            MethodDefinition dispatchMethod = CreateMethod(module, methodName, MethodAttributes.Public | MethodAttributes.HideBySig, logicMethod);
            MethodDefinition serverRpcMethod = CreateMethod(module, methodName + "ServerRpc", MethodAttributes.Private | MethodAttributes.HideBySig, logicMethod);
            MethodDefinition observersRpcMethod = CreateMethod(module, methodName + "ObserversRpc", MethodAttributes.Private | MethodAttributes.HideBySig, logicMethod);
            AddServerRpcAttribute(module, serverRpcMethod);
            // If the calling client already ran the logic locally, do not echo the
            // observers RPC back to its owned object and execute the logic twice.
            // Conversely, a caller which did not run locally must receive the RPC.
            AddObserversRpcAttribute(module, observersRpcMethod, runOnCallingClient);

            typeDef.Methods.Add(dispatchMethod);
            typeDef.Methods.Add(serverRpcMethod);
            typeDef.Methods.Add(observersRpcMethod);

            // Redirect all pre-existing callers of the original method to the new
            // public dispatch entry point BEFORE any new internal plumbing calls
            // exist, so the sweep can never catch (and mis-redirect) them.
            RewriteCallSites(logicMethod, dispatchMethod);

            EmitServerRpc(serverRpcMethod, logicMethod, observersRpcMethod, runOnServer);
            EmitObserversRpc(observersRpcMethod, logicMethod);
            EmitDispatch(module, dispatchMethod, logicMethod, serverRpcMethod, observersRpcMethod, runOnCallingServer, runOnCallingClient);
        }

        private static MethodDefinition CreateMethod(ModuleDefinition module, string methodName, MethodAttributes attributes, MethodDefinition template) {
            MethodDefinition result = new(methodName, attributes, module.ImportReference(template.ReturnType));
            foreach (ParameterDefinition parameter in template.Parameters)
                result.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, module.ImportReference(parameter.ParameterType)));
            return result;
        }

        /// <summary>
        /// Emits a runtime check against NetworkBehaviour.IsServer so the branch
        /// taken depends on who is actually calling, per invocation — not on a
        /// compile-time flag that would bake the same branch in for every caller.
        /// </summary>
        private static void EmitDispatch(ModuleDefinition module, MethodDefinition dispatchMethod, MethodDefinition logicMethod, MethodDefinition serverRpcMethod, MethodDefinition observersRpcMethod, bool runOnCallingServer, bool runOnCallingClient) {
            ILProcessor processor = dispatchMethod.Body.GetILProcessor();

            var isServerGetter = typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.IsServer))!.GetGetMethod();
            MethodReference isServerRef = module.ImportReference(isServerGetter);

            Instruction clientBranchStart = processor.Create(OpCodes.Nop);
            Instruction endLabel = processor.Create(OpCodes.Ret);

            // if (!base.IsServer) goto clientBranchStart;
            processor.Append(processor.Create(OpCodes.Ldarg_0));
            processor.Append(processor.Create(OpCodes.Call, isServerRef));
            processor.Append(processor.Create(OpCodes.Brfalse, clientBranchStart));

            // --- caller is the server/host ---
            if (runOnCallingServer)
                EmitCall(processor, logicMethod, dispatchMethod);
            EmitCall(processor, observersRpcMethod, dispatchMethod);
            processor.Append(processor.Create(OpCodes.Br, endLabel));

            // --- caller is a client ---
            processor.Append(clientBranchStart);
            if (runOnCallingClient)
                EmitCall(processor, logicMethod, dispatchMethod);
            EmitCall(processor, serverRpcMethod, dispatchMethod);

            processor.Append(endLabel);
        }

        private static void EmitServerRpc(MethodDefinition serverRpcMethod, MethodDefinition logicMethod, MethodDefinition observersRpcMethod, bool runOnServer) {
            ILProcessor processor = serverRpcMethod.Body.GetILProcessor();
            if (runOnServer)
                EmitCall(processor, logicMethod, serverRpcMethod);
            EmitCall(processor, observersRpcMethod, serverRpcMethod);
            processor.Append(processor.Create(OpCodes.Ret));
        }

        private static void EmitObserversRpc(MethodDefinition observersRpcMethod, MethodDefinition logicMethod) {
            ILProcessor processor = observersRpcMethod.Body.GetILProcessor();
            EmitCall(processor, logicMethod, observersRpcMethod);
            processor.Append(processor.Create(OpCodes.Ret));
        }

        private static void EmitCall(ILProcessor processor, MethodDefinition target, MethodDefinition argumentsFrom) {
            processor.Append(processor.Create(OpCodes.Ldarg_0));
            foreach (ParameterDefinition parameter in argumentsFrom.Parameters)
                processor.Append(processor.Create(OpCodes.Ldarg, parameter));
            processor.Append(processor.Create(OpCodes.Call, target));
        }

        private static void AddServerRpcAttribute(ModuleDefinition module, MethodDefinition methodDef) {
            var constructor = typeof(ServerRpcAttribute).GetConstructor(Type.EmptyTypes);
            methodDef.CustomAttributes.Add(new CustomAttribute(module.ImportReference(constructor)));
        }

        private static void AddObserversRpcAttribute(ModuleDefinition module, MethodDefinition methodDef, bool includeOwner) {
            var constructor = typeof(ObserversRpcAttribute).GetConstructor(Type.EmptyTypes);
            CustomAttribute attribute = new(module.ImportReference(constructor));
            attribute.Fields.Add(new CustomAttributeNamedArgument("ExcludeOwner", new CustomAttributeArgument(module.TypeSystem.Boolean, !includeOwner)));
            methodDef.CustomAttributes.Add(attribute);
        }

        private static bool ReadBool(CustomAttribute attribute, string name, bool fallback) {
            CustomAttributeNamedArgument property = attribute.Properties.FirstOrDefault(p => p.Name == name);
            return property.Name == null || property.Argument.Value == null ? fallback : (bool)property.Argument.Value;
        }

        /// <summary>
        /// Redirects existing callers of the original (now-renamed, now-private)
        /// logic method over to the new public dispatch method. Uses reference
        /// equality only — MetadataToken is not guaranteed stable for
        /// MethodDefinitions that haven't been written to the module yet, and
        /// comparing by token risked one RPC's plumbing call being mis-redirected
        /// into a different RPC's dispatch/server/observers chain, producing a
        /// call cycle and a stack overflow at runtime.
        /// </summary>
        private static void RewriteCallSites(MethodDefinition logicMethod, MethodDefinition dispatchMethod) {
            foreach (TypeDefinition type in GetAllTypes(logicMethod.Module.Types))
                foreach (MethodDefinition method in type.Methods) {
                    if (!method.HasBody || method == logicMethod || method == dispatchMethod)
                        continue;
                    foreach (Instruction instruction in method.Body.Instructions) {
                        if (instruction.Operand is MethodReference called && called == logicMethod)
                            instruction.Operand = dispatchMethod;
                    }
                }
        }

        private static IEnumerable<TypeDefinition> GetAllTypes(IEnumerable<TypeDefinition> types) {
            foreach (TypeDefinition type in types) {
                yield return type;
                foreach (TypeDefinition nestedType in GetAllTypes(type.NestedTypes))
                    yield return nestedType;
            }
        }
    }
}
