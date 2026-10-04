using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    internal sealed class ProductAssemblyIlReference : IEquatable<ProductAssemblyIlReference>
    {
        public ProductAssemblyIlReference(Type referrerType, Type referencedType)
        {
            ReferrerType = referrerType ?? throw new ArgumentNullException(nameof(referrerType));
            ReferencedType = referencedType ?? throw new ArgumentNullException(nameof(referencedType));
        }

        public Type ReferrerType { get; }
        public Type ReferencedType { get; }

        public bool Equals(ProductAssemblyIlReference other)
        {
            return other != null && ReferrerType == other.ReferrerType && ReferencedType == other.ReferencedType;
        }

        public override bool Equals(object obj) => Equals(obj as ProductAssemblyIlReference);
        public override int GetHashCode() => (ReferrerType.GetHashCode() * 397) ^ ReferencedType.GetHashCode();
    }

    internal static class ProductAssemblyIlScanner
    {
        private static readonly IReadOnlyDictionary<short, OpCode> Opcodes = BuildOpcodeTable();
        private static readonly HashSet<OperandType> TokenOperands = new HashSet<OperandType>
        {
            OperandType.InlineMethod,
            OperandType.InlineField,
            OperandType.InlineType,
            OperandType.InlineTok
        };

        public static IReadOnlyCollection<ProductAssemblyIlReference> Scan(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null) throw new ArgumentNullException(nameof(assemblies));
            var references = new HashSet<ProductAssemblyIlReference>();
            foreach (var assembly in assemblies)
            {
                if (assembly == null) throw new ArgumentException("Assembly collection contains null.", nameof(assemblies));
                foreach (var type in GetLoadableTypes(assembly))
                {
                    foreach (var method in GetMethods(type))
                    {
                        var body = method.GetMethodBody();
                        if (body == null) continue;
                        ScanMethod(type, method, body.GetILAsByteArray(), references);
                    }
                }
            }
            return references.ToArray();
        }

        // Internal test seam: it keeps malformed IL tests independent of compiler-generated methods.
        internal static void ScanIlBytes(Type declaringType, string methodName, Module module,
            Type[] genericTypeArguments, Type[] genericMethodArguments, byte[] il,
            ICollection<ProductAssemblyIlReference> references)
        {
            if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (il == null) throw new ArgumentNullException(nameof(il));
            if (references == null) throw new ArgumentNullException(nameof(references));
            ScanBytes(declaringType, methodName ?? "<unknown>", module,
                genericTypeArguments, genericMethodArguments, il, references);
        }

        private static void ScanMethod(Type declaringType, MethodBase method, byte[] il,
            ICollection<ProductAssemblyIlReference> references)
        {
            ScanBytes(declaringType, method.Name, method.Module,
                declaringType.IsGenericType ? declaringType.GetGenericArguments() : null,
                method.IsGenericMethod ? method.GetGenericArguments() : null,
                il, references);
        }

        private static void ScanBytes(Type declaringType, string methodName, Module module,
            Type[] genericTypeArguments, Type[] genericMethodArguments, byte[] il,
            ICollection<ProductAssemblyIlReference> references)
        {
            var offset = 0;
            while (offset < il.Length)
            {
                var instructionOffset = offset;
                var opcode = ReadOpcode(declaringType, methodName, il, ref offset);
                if (opcode == OpCodes.Calli)
                    Fail(declaringType, methodName, instructionOffset, "calli is not supported");

                var operandOffset = offset;
                var token = 0;
                if (TokenOperands.Contains(opcode.OperandType))
                {
                    token = ReadInt32(declaringType, methodName, il, ref offset, instructionOffset);
                    var resolved = ResolveToken(opcode.OperandType, module, token,
                        genericTypeArguments, genericMethodArguments,
                        declaringType, methodName, instructionOffset);
                    AddReferencedTypes(declaringType, resolved, references);
                }
                else
                {
                    offset = AdvanceOperand(declaringType, methodName, il, offset, opcode.OperandType, instructionOffset);
                }

                if (offset <= operandOffset && opcode.OperandType != OperandType.InlineNone)
                    Fail(declaringType, methodName, instructionOffset, "operand did not advance");
            }
        }

        private static object ResolveToken(OperandType operandType, Module module, int token,
            Type[] genericTypeArguments, Type[] genericMethodArguments, Type declaringType,
            string methodName, int offset)
        {
            try
            {
                switch (operandType)
                {
                    case OperandType.InlineMethod:
                        return module.ResolveMethod(token, genericTypeArguments, genericMethodArguments);
                    case OperandType.InlineField:
                        return module.ResolveField(token, genericTypeArguments, genericMethodArguments);
                    case OperandType.InlineType:
                        return module.ResolveType(token, genericTypeArguments, genericMethodArguments);
                    case OperandType.InlineTok:
                        return module.ResolveMember(token, genericTypeArguments, genericMethodArguments);
                    default:
                        throw new InvalidOperationException("unsupported token operand");
                }
            }
            catch (Exception exception)
            {
                Fail(declaringType, methodName, offset, "token resolution failed: " + exception.Message);
                return null;
            }
        }

        private static void AddReferencedTypes(Type referrer, object resolved,
            ICollection<ProductAssemblyIlReference> references)
        {
            if (resolved is MethodBase method)
            {
                AddType(referrer, method.DeclaringType, references);
                foreach (var parameter in method.GetParameters()) AddType(referrer, parameter.ParameterType, references);
                AddType(referrer, (method as MethodInfo)?.ReturnType, references);
            }
            else if (resolved is FieldInfo field)
            {
                AddType(referrer, field.DeclaringType, references);
                AddType(referrer, field.FieldType, references);
            }
            else if (resolved is Type type)
            {
                AddType(referrer, type, references);
            }
        }

        private static void AddType(Type referrer, Type type, ICollection<ProductAssemblyIlReference> references)
        {
            if (type == null) return;
            var normalized = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (normalized != referrer) references.Add(new ProductAssemblyIlReference(referrer, normalized));
            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments()) AddType(referrer, argument, references);
            if (type.IsArray || type.IsByRef || type.IsPointer) AddType(referrer, type.GetElementType(), references);
        }

        private static int AdvanceOperand(Type type, string method, byte[] il, int offset,
            OperandType operandType, int instructionOffset)
        {
            var size = operandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI => 1,
                OperandType.ShortInlineVar => 1,
                OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI => 4,
                OperandType.InlineBrTarget => 4,
                OperandType.InlineField => 4,
                OperandType.InlineI8 => 8,
                OperandType.InlineMethod => 4,
                OperandType.InlineSig => throw new InvalidOperationException("signature token is not supported"),
                OperandType.InlineString => 4,
                OperandType.InlineSwitch => ReadSwitchSize(type, method, il, offset, instructionOffset),
                OperandType.InlineTok => 4,
                OperandType.InlineType => 4,
                OperandType.ShortInlineR => 4,
                OperandType.InlineR => 8,
                _ => throw new InvalidOperationException("unknown operand type " + operandType)
            };
            EnsureAvailable(type, method, il, offset, size, instructionOffset);
            return offset + size;
        }

        private static int ReadSwitchSize(Type type, string method, byte[] il, int offset, int instructionOffset)
        {
            EnsureAvailable(type, method, il, offset, 4, instructionOffset);
            var count = BitConverter.ToInt32(il, offset);
            if (count < 0 || count > (il.Length - offset - 4) / 4)
                Fail(type, method, instructionOffset, "switch operand is truncated");
            return 4 + count * 4;
        }

        private static int ReadInt32(Type type, string method, byte[] il, ref int offset, int instructionOffset)
        {
            EnsureAvailable(type, method, il, offset, 4, instructionOffset);
            var value = BitConverter.ToInt32(il, offset);
            offset += 4;
            return value;
        }

        private static OpCode ReadOpcode(Type type, string method, byte[] il, ref int offset)
        {
            var opcodeOffset = offset;
            var value = il[offset++];
            short key = value == 0xFE
                ? (short)(0xFE00 | ReadByte(type, method, il, ref offset, opcodeOffset))
                : value;
            if (!Opcodes.TryGetValue(key, out var opcode))
                Fail(type, method, opcodeOffset, "unknown opcode 0x" + key.ToString("X4"));
            return opcode;
        }

        private static byte ReadByte(Type type, string method, byte[] il, ref int offset, int instructionOffset)
        {
            EnsureAvailable(type, method, il, offset, 1, instructionOffset);
            return il[offset++];
        }

        private static void EnsureAvailable(Type type, string method, byte[] il, int offset, int size, int instructionOffset)
        {
            if (size < 0 || offset < 0 || size > il.Length - offset)
                Fail(type, method, instructionOffset, "operand is truncated");
        }

        private static void Fail(Type type, string method, int offset, string reason)
        {
            throw new InvalidOperationException($"IL scan failed in {type.FullName}.{method} at IL_{offset:X4}: {reason}");
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }

        private static IEnumerable<MethodBase> GetMethods(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var constructor in type.GetConstructors(flags)) yield return constructor;
            foreach (var method in type.GetMethods(flags)) yield return method;
        }

        private static IReadOnlyDictionary<short, OpCode> BuildOpcodeTable()
        {
            var result = new Dictionary<short, OpCode>();
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(OpCode))
                {
                    var opcode = (OpCode)field.GetValue(null);
                    result[opcode.Value] = opcode;
                }
            }
            return result;
        }
    }
}
