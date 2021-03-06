﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

using Internal.IL;
using Internal.IL.Stubs;
using Internal.TypeSystem;

namespace ILCompiler
{
    public class RemovingILProvider : ILProvider
    {
        private readonly ILProvider _baseILProvider;
        private readonly RemovedFeature _removedFeature;

        public RemovingILProvider(ILProvider baseILProvider, RemovedFeature feature)
        {
            _baseILProvider = baseILProvider;
            _removedFeature = feature;
        }

        public override MethodIL GetMethodIL(MethodDesc method)
        {
            switch (GetAction(method))
            {
                case RemoveAction.Nothing:
                    return _baseILProvider.GetMethodIL(method);

                case RemoveAction.ConvertToStub:
                    if (method.Signature.ReturnType.IsVoid)
                        return new ILStubMethodIL(method, new byte[] { (byte)ILOpcode.ret }, Array.Empty<LocalVariableDefinition>(), null);
                    else if (method.Signature.ReturnType.Category == TypeFlags.Boolean)
                        return new ILStubMethodIL(method, new byte[] { (byte)ILOpcode.ldc_i4_0, (byte)ILOpcode.ret }, Array.Empty<LocalVariableDefinition>(), null);
                    else
                        goto default;

                case RemoveAction.ConvertToTrueStub:
                    return new ILStubMethodIL(method, new byte[] { (byte)ILOpcode.ldc_i4_1, (byte)ILOpcode.ret }, Array.Empty<LocalVariableDefinition>(), null);

                case RemoveAction.ConvertToGetResourceStringStub:
                    return new ILStubMethodIL(method, new byte[] {
                        (byte)ILOpcode.ldarg_0,
                        (byte)ILOpcode.ret },
                        Array.Empty<LocalVariableDefinition>(), null);

                case RemoveAction.ConvertToThrow:
                    return new ILStubMethodIL(method, new byte[] { (byte)ILOpcode.ldnull, (byte)ILOpcode.throw_ }, Array.Empty<LocalVariableDefinition>(), null);

                default:
                    throw new NotImplementedException();
            }
        }

        private RemoveAction GetAction(MethodDesc method)
        {
            TypeDesc owningType = method.OwningType;

            if ((_removedFeature & RemovedFeature.Etw) != 0)
            {
                if (!method.Signature.IsStatic)
                {
                    if (IsEventSourceType(owningType))
                    {
                        if (method.IsConstructor)
                            return RemoveAction.ConvertToStub;

                        if (method.Name == "IsEnabled" || method.IsFinalizer || method.Name == "Dispose")
                            return RemoveAction.ConvertToStub;

                        return RemoveAction.ConvertToThrow;
                    }
                    else if (IsEventSourceImplementation(owningType))
                    {
                        if (method.IsFinalizer)
                            return RemoveAction.ConvertToStub;

                        if (method.IsConstructor)
                            return RemoveAction.ConvertToStub;

                        if (method.HasCustomAttribute("System.Diagnostics.Tracing", "NonEventAttribute"))
                            return RemoveAction.Nothing;

                        return RemoveAction.ConvertToThrow;
                    }
                }
            }

            if ((_removedFeature & RemovedFeature.FrameworkResources) != 0)
            {
                if (method.Signature.IsStatic &&
                    owningType is Internal.TypeSystem.Ecma.EcmaType mdType &&
                    mdType.Name == "SR" && mdType.Namespace == "System" &&
                    FrameworkStringResourceBlockingPolicy.IsFrameworkAssembly(mdType.EcmaModule))
                {
                    if (method.Name == "UsingResourceKeys")
                        return RemoveAction.ConvertToTrueStub;
                    else if (method.Name == "GetResourceString" && method.Signature.Length == 2)
                        return RemoveAction.ConvertToGetResourceStringStub;

                    return RemoveAction.Nothing;
                }
            }

            return RemoveAction.Nothing;
        }

        private object _eventSourceType;
        private bool IsEventSourceType(TypeDesc type)
        {
            if (_eventSourceType == null)
                _eventSourceType = type.Context.SystemModule.GetType("System.Diagnostics.Tracing", "EventSource") ?? new object();

            return Object.ReferenceEquals(type, _eventSourceType);
        }
        
        private bool IsEventSourceImplementation(TypeDesc type)
        {
            try
            {
                TypeDesc baseType = type.BaseType;
                while (baseType != null)
                {
                    if (IsEventSourceImplementation(baseType))
                        return true;
                    baseType = baseType.BaseType;
                }
            }
            catch (TypeSystemException)
            {
            }

            return false;
        }

        private enum RemoveAction
        {
            Nothing,
            ConvertToStub,
            ConvertToThrow,

            ConvertToTrueStub,
            ConvertToGetResourceStringStub,
        }
    }

    [Flags]
    public enum RemovedFeature
    {
        Etw = 0x1,
        FrameworkResources = 0x2,
    }
}
