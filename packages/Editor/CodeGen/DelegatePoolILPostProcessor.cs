using Katuusagi.ILPostProcessorCommon.Editor;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Katuusagi.Pool.Editor
{
    internal class DelegatePoolILPostProcessor : ILPostProcessor
    {
        private enum PoolType
        {
            Default,
            ThreadStatic,
            Concurrent
        }

        private ModuleDefinition _delegatePoolModule = null;
        private IMetadataScope _delegatePoolScope = null;
        private TypeReference _ireferenceHandlerRef = null;

        public override ILPostProcessor GetInstance() => this;
        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            return compiledAssembly.References.Any(v => v.EndsWith("Katuusagi.DelegatePool.dll"));
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            if (!WillProcess(compiledAssembly))
            {
                return null;
            }

            try
            {
                ILPPUtils.InitLog<DelegatePoolILPostProcessor>(compiledAssembly);
                using (var assembly = ILPPUtils.LoadAssemblyDefinition(compiledAssembly))
                {
                    var module = assembly.MainModule;
                    foreach (var type in assembly.Modules.SelectMany(v => v.Types).GetAllTypes().ToArray())
                    {
                        foreach (var method in type.Methods.ToArray())
                        {
                            var body = method.Body;
                            if (body == null)
                            {
                                continue;
                            }

                            var ilProcessor = body.GetILProcessor();
                            bool isChanged = false;
                            var instructions = body.Instructions;
                            for (var i = 0; i < instructions.Count; ++i)
                            {
                                var instruction = instructions[i];
                                int diff = 0;
                                isChanged = DelegatePoolProcess(body, method, instruction, ref diff) || isChanged;
                                i += diff;
                            }

                            if (isChanged)
                            {
                                instructions = body.Instructions;
                                for (var i = 0; i < instructions.Count; ++i)
                                {
                                    var instruction = instructions[i];
                                    int diff = 0;
                                    LambdaPoolProcess(body, method, instruction, ref diff);
                                    i += diff;
                                }

                                ILPPUtils.ResolveInstructionOpCode(instructions);
                            }
                        }
                    }

                    var pe  = new MemoryStream();
                    var pdb = new MemoryStream();
                    var writeParameter = new WriterParameters
                    {
                        SymbolWriterProvider = new PortablePdbWriterProvider(),
                        SymbolStream         = pdb,
                        WriteSymbols         = true
                    };

                    assembly.Write(pe, writeParameter);
                    return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), ILPPUtils.Logger.Messages);
                }
            }
            catch (Exception e)
            {
                ILPPUtils.LogException(e);
            }
            return new ILPostProcessResult(null, ILPPUtils.Logger.Messages);
        }

        private bool DelegatePoolProcess(MethodBody body, MethodDefinition method, Instruction instruction, ref int diff)
        {
            if (instruction.OpCode != OpCodes.Call ||
                !(instruction.Operand is MethodReference getRef) ||
                getRef.DeclaringType.Namespace != "Katuusagi.Pool" ||
                (getRef.DeclaringType.Name != "DelegatePool`1" && getRef.DeclaringType.Name != "ThreadStaticDelegatePool`1" && getRef.DeclaringType.Name != "ConcurrentDelegatePool`1") ||
                getRef.Name != "Get" ||
                getRef.Parameters.Count != 2)
            {
                return false;
            }

            _delegatePoolModule = getRef.DeclaringType.Module;
            _delegatePoolScope = getRef.DeclaringType.Scope;

            instruction.TryGetPushArgumentInstruction(0, out var newObj);
            bool isCacheGenerated = false;
            if (newObj.OpCode == OpCodes.Dup)
            {
                var stsfld = newObj.GetNext();
                if (stsfld.OpCode == OpCodes.Stsfld &&
                    stsfld.Operand is FieldReference generatedField &&
                    generatedField.DeclaringType.Name.StartsWith("<>", StringComparison.Ordinal))
                {
                    newObj = newObj.GetPrev();
                    isCacheGenerated = true;
                }
            }

            if (newObj.OpCode != OpCodes.Newobj ||
                !(newObj.Operand is MethodReference ctorRef))
            {
                ILPPUtils.LogError("DELEGATEPOOL001", "DelegatePool failed.", "\"DelegatePool.Get\" must be assigned a method directly.", method, instruction);
                return false;
            }

            var ctorDef = ctorRef.Resolve();
            var delegateTypeDef = ctorDef.DeclaringType;
            var delegateBaseTypeName = delegateTypeDef.BaseType.FullName;
            if (delegateBaseTypeName != "System.Delegate" &&
                delegateBaseTypeName != "System.MulticastDelegate")
            {
                ILPPUtils.LogError("DELEGATEPOOL002", "DelegatePool failed.", "\"DelegatePool\" supports only Delegate type.", method, instruction);
                return false;
            }

            var ldftn = newObj.GetPrev();
            if ((ldftn.OpCode != OpCodes.Ldftn && ldftn.OpCode != OpCodes.Ldvirtftn) ||
                !(ldftn.Operand is MethodReference loadedMethodRef))
            {
                ILPPUtils.LogError("DELEGATEPOOL003", "DelegatePool failed.", "\"DelegatePool.Get\" must be assigned a method directly.", method, instruction);
                return false;
            }

            var targetTypeRef = loadedMethodRef.DeclaringType;
            var isStruct = targetTypeRef.IsStruct();
            var isSealed = targetTypeRef.IsSealed();
            if (ldftn.OpCode == OpCodes.Ldvirtftn && (isStruct || isSealed))
            {
                var dup = ldftn.GetPrev();
                if (dup.OpCode == OpCodes.Dup)
                {
                    ldftn.OpCode = OpCodes.Ldftn;
                    dup.OpCode = OpCodes.Nop;
                    dup.Operand = null;
                }
                else
                {
                    ILPPUtils.LogError("DELEGATEPOOL499", "DelegatePool failed.", "Unknown implementation.", method, instruction);
                    return false;
                }
            }

            if (isCacheGenerated)
            {
                instruction.TryGetPushArgumentInstruction(1, out var ldloca);
                var seek = ldftn.GetPrev();
                while (seek != null)
                {
                    if (seek.OpCode == OpCodes.Brtrue ||
                        seek.OpCode == OpCodes.Brtrue_S &&
                        seek.Operand == ldloca)
                    {
                        break;
                    }

                    seek = seek.GetPrev();
                }

                if (seek != null)
                {
                    {
                        var dup = newObj.GetNext();
                        if (dup.OpCode == OpCodes.Dup)
                        {
                            dup.OpCode = OpCodes.Nop;
                            dup.Operand = null;
                        }
                        else
                        {
                            ILPPUtils.LogError("DELEGATEPOOL499", "DelegatePool failed.", "Unknown implementation.", method, instruction);
                            return false;
                        }

                        var stsfld = dup.GetNext();
                        if (stsfld.OpCode == OpCodes.Stsfld)
                        {
                            stsfld.OpCode = OpCodes.Nop;
                            stsfld.Operand = null;
                        }
                        else
                        {
                            ILPPUtils.LogError("DELEGATEPOOL499", "DelegatePool failed.", "Unknown implementation.", method, instruction);
                            return false;
                        }
                    }

                    {
                        var pop = seek.GetNext();
                        if (pop.OpCode == OpCodes.Pop)
                        {
                            pop.OpCode = OpCodes.Nop;
                            pop.Operand = null;
                        }
                        else
                        {
                            ILPPUtils.LogError("DELEGATEPOOL499", "DelegatePool failed.", "Unknown implementation.", method, instruction);
                            return false;
                        }
                    }

                    {
                        var br = seek;
                        if (br.OpCode == OpCodes.Brtrue_S ||
                            br.OpCode == OpCodes.Brtrue)
                        {
                            br.OpCode = OpCodes.Nop;
                            br.Operand = null;
                        }
                        else
                        {
                            ILPPUtils.LogError("DELEGATEPOOL499", "DelegatePool failed.", "Unknown implementation.", method, instruction);
                            return false;
                        }

                        var dup = br.GetPrev();
                        if (dup.OpCode == OpCodes.Dup)
                        {
                            dup.OpCode = OpCodes.Nop;
                            dup.Operand = null;
                        }
                        else
                        {
                            ILPPUtils.LogError("DELEGATEPOOL499", "DelegatePool failed.", "Unknown implementation.", method, instruction);
                            return false;
                        }

                        var ldsfld = dup.GetPrev();
                        if (ldsfld.OpCode == OpCodes.Ldsfld)
                        {
                            ldsfld.OpCode = OpCodes.Nop;
                            ldsfld.Operand = null;
                        }
                        else
                        {
                            ILPPUtils.LogError("DELEGATEPOOL499", "DelegatePool failed.", "Unknown implementation.", method, instruction);
                            return false;
                        }
                    }
                }
                else
                {
                    ILPPUtils.LogError("DELEGATEPOOL499", "DelegatePool failed.", "Unknown implementation.", method, instruction);
                    return false;
                }
            }

            if (isStruct)
            {
                newObj.OpCode = OpCodes.Nop;
                newObj.Operand = null;
            }
            else
            {
                newObj.OpCode = OpCodes.Ldnull;
                newObj.Operand = null;
            }

            var module = method.Module;
            getRef = GetDelegatePoolGet(module, getRef, targetTypeRef);
            instruction.Operand = getRef;

            if (!isStruct)
            {
                return true;
            }

            instruction.TryGetPushArgumentInstruction(0, out var box);
            if (box.OpCode != OpCodes.Box)
            {
                ILPPUtils.LogError("DELEGATEPOOL499", "DelegatePool failed.", "Unknown implementation.", method, instruction);
                return true;
            }

            var target = box.GetPrev();
            if (target.OpCode == OpCodes.Ldfld)
            {
                box.OpCode = OpCodes.Nop;
                box.Operand = null;

                target.OpCode = OpCodes.Ldflda;
                return true;
            }

            if (target.OpCode == OpCodes.Ldsfld)
            {
                box.OpCode = OpCodes.Nop;
                box.Operand = null;

                target.OpCode = OpCodes.Ldsflda;
                return true;
            }

            var ldlocVar = ILPPUtils.GetVariableFromLdloc(target, body);
            if (ldlocVar != null)
            {
                box.OpCode = OpCodes.Nop;
                box.Operand = null;

                var ldlocaTmp = ILPPUtils.LoadLocalAddress(ldlocVar);
                target.OpCode = ldlocaTmp.OpCode;
                target.Operand = ldlocaTmp.Operand;
                return true;
            }

            var ldargParam = ILPPUtils.GetArgumentFromLdarg(target, method);
            if (ldargParam != null)
            {
                box.OpCode = OpCodes.Nop;
                box.Operand = null;

                var ldlocaTmp = ILPPUtils.LoadArgumentAddress(ldargParam);
                target.OpCode = ldlocaTmp.OpCode;
                target.Operand = ldlocaTmp.Operand;
                return true;
            }

            var ilProcessor = body.GetILProcessor();
            var targetInstanceVar = new VariableDefinition(targetTypeRef);
            body.Variables.Add(targetInstanceVar);
            var stlocTmp = ILPPUtils.SetLocal(targetInstanceVar);
            box.OpCode = stlocTmp.OpCode;
            box.Operand = stlocTmp.Operand;

            ilProcessor.InsertBefore(box, ILPPUtils.LoadLocalAddress(targetInstanceVar));
            ++diff;
            return true;
        }

        private void LambdaPoolProcess(MethodBody body, MethodDefinition method, Instruction instruction, ref int diff)
        {
            if (instruction.OpCode != OpCodes.Newobj ||
                !(instruction.Operand is MethodReference cctor) ||
                !cctor.DeclaringType.Name.StartsWith("<>", StringComparison.Ordinal))
            {
                return;
            }

            var ilProcessor = body.GetILProcessor();
            var lambdaInstanceTypeRef = cctor.DeclaringType;
            var stloc = instruction.GetNext();
            var gets = FindDelegatePoolGetClassOnlyAll(stloc).ToArray();
            VariableDefinition lambdaInstanceVar = null;
            if (stloc.OpCode == OpCodes.Dup)
            {
                lambdaInstanceVar = new VariableDefinition(lambdaInstanceTypeRef);
                body.Variables.Add(lambdaInstanceVar);
                ilProcessor.InsertAfter(stloc, ILPPUtils.LoadLocal(lambdaInstanceVar));
                ilProcessor.InsertAfter(stloc, ILPPUtils.LoadLocal(lambdaInstanceVar));
            }
            else
            {
                lambdaInstanceVar = ILPPUtils.GetVariableFromStloc(stloc, body);
                if (lambdaInstanceVar == null)
                {
                    ILPPUtils.LogWarning("DELEGATEPOOL599", "DelegatePool failed.", "Unknown lambda implementation.", method, gets.FirstOrDefault());
                    return;
                }

                var ldlocs = FindLdlocs(body, lambdaInstanceVar).ToArray();
                var isPoolable = ldlocs.All(v => IsAllowedDelegatePoolLdloc(v, lambdaInstanceTypeRef));
                if (!isPoolable)
                {
                    return;
                }
            }

            var handlerVar = new VariableDefinition(_ireferenceHandlerRef);
            body.Variables.Add(handlerVar);
            ilProcessor.InsertBefore(instruction, ILPPUtils.LoadLocalAddress(handlerVar));

            var ldLambdaInstanceTmp = ILPPUtils.LoadLocalAddress(lambdaInstanceVar);
            instruction.OpCode = ldLambdaInstanceTmp.OpCode;
            instruction.Operand = ldLambdaInstanceTmp.Operand;

            var poolType = GetPoolType(gets);
            var poolGet = GetCountPoolGet(method.Module, poolType, lambdaInstanceTypeRef);
            stloc.OpCode = OpCodes.Call;
            stloc.Operand = poolGet;

            foreach (var get in gets)
            {
                get.TryGetPushArgumentInstruction(2, out var ldHandlerVar);
                var ldHandlerTmp = ILPPUtils.LoadLocal(handlerVar);
                ldHandlerVar.OpCode = ldHandlerTmp.OpCode;
                ldHandlerVar.Operand = ldHandlerTmp.Operand;
            }

            var last = body.Instructions.Last();
            var lastRet = Instruction.Create(OpCodes.Ret);
            ilProcessor.Append(lastRet);

            var start = ILPPUtils.LoadLocal(handlerVar);
            ilProcessor.InsertBefore(lastRet, start);
            ++diff;
            var poolReturn = GetCountPoolReturn(method.Module, poolType, lambdaInstanceTypeRef);
            ilProcessor.InsertBefore(lastRet, Instruction.Create(OpCodes.Call, poolReturn));
            ++diff;
            var end = Instruction.Create(OpCodes.Endfinally);
            ilProcessor.InsertBefore(lastRet, end);
            ++diff;

            body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = body.Instructions.First(),
                TryEnd = start,
                HandlerStart = start,
                HandlerEnd = lastRet,
            });

            for (int i = 0; body.Instructions[i] != last.GetNext(); ++i)
            {
                var it = body.Instructions[i];
                if (it.OpCode == OpCodes.Ret)
                {
                    it.OpCode = OpCodes.Leave;
                    it.Operand = lastRet;
                }
            }
        }

        private PoolType GetPoolType(Instruction[] gets)
        {
            PoolType poolType = PoolType.Default;
            foreach (var get in gets)
            {
                if (!(get.Operand is MethodReference method))
                {
                    continue;
                }

                switch (method.DeclaringType.Name)
                {
                    case "ThreadStaticDelegatePool`1":
                        poolType = PoolType.ThreadStatic;
                        break;
                    case "ConcurrentDelegatePool`1":
                        return PoolType.Concurrent;
                }
            }

            return poolType;
        }

        private MethodReference GetDelegatePoolGet(ModuleDefinition currentModule, MethodReference getRef, TypeReference targetTypeRef)
        {
            var handlerType = getRef.ReturnType;
            var poolType = getRef.DeclaringType;
            var resultType = getRef.Parameters[1].ParameterType;
            if (targetTypeRef.IsStruct())
            {
                getRef = new MethodReference("GetStructOnly", handlerType, poolType);
                var ttarget = new GenericParameter("TTarget", getRef);

                getRef.GenericParameters.Add(ttarget);
                getRef.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, ttarget.MakeByReferenceType()));
                getRef.Parameters.Add(new ParameterDefinition("pFunc", ParameterAttributes.None, currentModule.TypeSystem.IntPtr));
                getRef.Parameters.Add(new ParameterDefinition("result", ParameterAttributes.Retval, resultType));
            }
            else
            {
                getRef = new MethodReference("GetClassOnly", handlerType, poolType);
                var ttarget = new GenericParameter("TTarget", getRef);

                var referenceHandler = GetReferenceHandlerType(currentModule, poolType.Module, poolType.Scope);

                getRef.GenericParameters.Add(ttarget);
                getRef.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, ttarget));
                getRef.Parameters.Add(new ParameterDefinition("pFunc", ParameterAttributes.None, currentModule.TypeSystem.IntPtr));
                getRef.Parameters.Add(new ParameterDefinition("lambda", ParameterAttributes.None, referenceHandler));
                getRef.Parameters.Add(new ParameterDefinition("result", ParameterAttributes.Retval, resultType));
            }

            getRef = getRef.MakeGenericInstanceMethod(new TypeReference[] { targetTypeRef });
            return currentModule.ImportReference(getRef);
        }

        private TypeReference GetCountPoolType(ModuleDefinition currentModule, PoolType poolType)
        {
            var poolName = "CountPool`1";
            switch (poolType)
            {
                case PoolType.ThreadStatic:
                    poolName = "ThreadStaticCountPool`1";
                    break;
                case PoolType.Concurrent:
                    poolName = "ConcurrentCountPool`1";
                    break;
            }

            var countPoolRef = new TypeReference("Katuusagi.Pool", poolName, _delegatePoolModule, _delegatePoolScope, false);
            countPoolRef.GenericParameters.Add(new GenericParameter("T", countPoolRef));
            countPoolRef = currentModule.ImportReference(countPoolRef);
            return countPoolRef;
        }

        private MethodReference GetCountPoolGet(ModuleDefinition currentModule, PoolType poolType, TypeReference instanceTypeRef)
        {
            var countPoolRef = GetCountPoolType(currentModule, poolType);
            var countPoolDef = countPoolRef.Resolve();
            var countPoolGetDef = countPoolDef.Methods.FirstOrDefault(v => v.Name == "Get" && v.Parameters.Count == 2);

            countPoolRef = countPoolRef.MakeGenericInstanceType(instanceTypeRef);
            var countPoolGetRef = new MethodReference(countPoolGetDef.Name, currentModule.TypeSystem.Void, countPoolRef);
            var parameter0 = countPoolGetDef.Parameters[0];
            var parameter1 = countPoolGetDef.Parameters[1];
            countPoolGetRef.Parameters.Add(new ParameterDefinition(parameter0.Name, parameter0.Attributes, _ireferenceHandlerRef.MakeByReferenceType()));
            countPoolGetRef.Parameters.Add(new ParameterDefinition(parameter1.Name, parameter1.Attributes, parameter1.ParameterType));
            countPoolGetRef = currentModule.ImportReference(countPoolGetRef);
            return countPoolGetRef;
        }

        private MethodReference GetCountPoolReturn(ModuleDefinition currentModule, PoolType poolType, TypeReference instanceTypeRef)
        {
            var countPoolRef = GetCountPoolType(currentModule, poolType);
            var countPoolDef = countPoolRef.Resolve();
            var countPoolReturnDef = countPoolDef.Methods.FirstOrDefault(v => v.Name == "Return" && v.Parameters.Count == 1);

            countPoolRef = countPoolRef.MakeGenericInstanceType(instanceTypeRef);
            var countPoolReturnRef = new MethodReference(countPoolReturnDef.Name, currentModule.TypeSystem.Void, countPoolRef);
            var parameter = countPoolReturnDef.Parameters[0];
            countPoolReturnRef.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, _ireferenceHandlerRef));
            countPoolReturnRef = currentModule.ImportReference(countPoolReturnRef);
            return countPoolReturnRef;
        }

        private TypeReference GetReferenceHandlerType(ModuleDefinition currentModule, ModuleDefinition poolModule, IMetadataScope scope)
        {
            if (_ireferenceHandlerRef == null)
            {
                var referenceHandlerRef = new TypeReference("Katuusagi.Pool", "IReferenceHandler", poolModule, scope, false);
                _ireferenceHandlerRef = currentModule.ImportReference(referenceHandlerRef);
            }

            return _ireferenceHandlerRef;
        }

        private IEnumerable<Instruction> FindLdlocs(MethodBody body, VariableDefinition variable)
        {
            var instructions = body.Instructions;
            for (int i = 0; i < instructions.Count; ++i)
            {
                var instruction = instructions[i];
                var cmp = ILPPUtils.GetVariableFromLdloc(instruction, body);
                if (variable == cmp)
                {
                    yield return instruction;
                }
            }
        }

        private bool IsAllowedDelegatePoolLdloc(Instruction instruction, TypeReference lambdaInstanceType)
        {
            var result = IsValueOfStfld(lambdaInstanceType, instruction) ||
                         IsValueOfLdfld(lambdaInstanceType, instruction) ||
                         IsArgumentOfDelegatePoolGet(0, instruction);
            return result;
        }

        private bool IsValueOfStfld(TypeReference declaringType, Instruction instruction)
        {
            var stfld = FindStfld(instruction, declaringType);
            if (stfld == null ||
                !stfld.TryGetStackPushedInstruction(-2, out var pushedValue))
            {
                return false;
            }

            return pushedValue == instruction;
        }

        private bool IsValueOfLdfld(TypeReference declaringType, Instruction instruction)
        {
            var ldfld = FindLdfld(instruction, declaringType);
            if (ldfld == null ||
                !ldfld.TryGetStackPushedInstruction(-1, out var pushedValue))
            {
                return false;
            }

            return pushedValue == instruction;
        }

        private bool IsArgumentOfDelegatePoolGet(int argNumber, Instruction instruction)
        {
            var call = FindDelegatePoolGetClassOnly(instruction);
            if (call == null ||
                !call.TryGetPushArgumentInstruction(argNumber, out var arg))
            {
                return false;
            }

            return arg == instruction;
        }

        private Instruction FindDelegatePoolGetClassOnly(Instruction instruction)
        {
            var it = instruction.GetNext();
            while (it != null)
            {
                if (it.OpCode == OpCodes.Call &&
                    it.Operand is MethodReference getRef &&
                    getRef.DeclaringType.Namespace == "Katuusagi.Pool" &&
                    (getRef.DeclaringType.Name == "DelegatePool`1" || getRef.DeclaringType.Name == "ThreadStaticDelegatePool`1" || getRef.DeclaringType.Name == "ConcurrentDelegatePool`1") &&
                    getRef.Name == "GetClassOnly")
                {
                    return it;
                }

                it = it.GetNext();
            }

            return null;
        }

        private IEnumerable<Instruction> FindDelegatePoolGetClassOnlyAll(Instruction instruction)
        {
            var it = instruction.GetNext();
            while (it != null)
            {
                if (it.OpCode == OpCodes.Call &&
                    it.Operand is MethodReference getRef &&
                    getRef.DeclaringType.Namespace == "Katuusagi.Pool" &&
                    (getRef.DeclaringType.Name == "DelegatePool`1" ||
                    getRef.DeclaringType.Name == "ThreadStaticDelegatePool`1" ||
                    getRef.DeclaringType.Name == "ConcurrentDelegatePool`1") &&
                    getRef.Name == "GetClassOnly")
                {
                    yield return it;
                }

                it = it.GetNext();
            }
        }

        private Instruction FindStfld(Instruction instruction, TypeReference declairingType)
        {
            var it = instruction.GetNext();
            while (it != null)
            {
                if (it.OpCode == OpCodes.Stfld &&
                    it.Operand is FieldReference fieldRef &&
                    fieldRef.DeclaringType.Is(declairingType))
                {
                    return it;
                }

                it = it.GetNext();
            }

            return null;
        }

        private Instruction FindLdfld(Instruction instruction, TypeReference declairingType)
        {
            var it = instruction.GetNext();
            while (it != null)
            {
                if ((it.OpCode == OpCodes.Ldfld || it.OpCode == OpCodes.Ldflda) &&
                    it.Operand is FieldReference fieldRef &&
                    fieldRef.DeclaringType.Is(declairingType))
                {
                    return it;
                }

                it = it.GetNext();
            }

            return null;
        }
    }
}
