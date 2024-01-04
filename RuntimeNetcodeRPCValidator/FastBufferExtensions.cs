using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using Unity.Collections;
using Unity.Netcode;

namespace RuntimeNetcodeRPCValidator
{
    public static class FastBufferExtensions
    {
        /// <summary>
        /// Writes a System.Serializable object to a <see cref="FastBufferWriter"/>.
        /// </summary>
        /// <param name="fastBufferWriter">The <see cref="FastBufferWriter"/> to write to.</param>
        /// <param name="serializable">The serializable object to write.</param>
        private static void WriteSystemSerializable(this FastBufferWriter fastBufferWriter, object serializable)
        {
            var formatter = new BinaryFormatter();
            using var stream = new MemoryStream();

            formatter.Serialize(stream, serializable);
            var paramBytes = stream.ToArray();

            fastBufferWriter.WriteValueSafe(paramBytes.Length);
            fastBufferWriter.WriteBytes(paramBytes);
        }

        /// <summary>
        /// Reads a System.Serializable object from a FastBufferReader.
        /// </summary>
        /// <param name="fastBufferReader">The FastBufferReader to read from.</param>
        /// <param name="serializable">The deserialized object.</param>
        private static void ReadSystemSerializable(this FastBufferReader fastBufferReader, out object serializable)
        {
            fastBufferReader.ReadValueSafe(out int byteLength);
            var paramBytes = new byte[byteLength];
            fastBufferReader.ReadBytes(ref paramBytes, byteLength);

            using var stream = new MemoryStream(paramBytes);
            stream.Seek(0, 0);
            var formatter = new BinaryFormatter();

            serializable = formatter.Deserialize(stream);
        }

        /// <summary>
        /// Writes a network serializable object to a network buffer using the Netcode protocol.
        /// </summary>
        /// <param name="fastBufferWriter">The FastBufferWriter used to write the serialized object to.</param>
        /// <param name="networkSerializable">The object to be serialized.</param>
        private static void WriteNetcodeSerializable(this FastBufferWriter fastBufferWriter, object networkSerializable)
        {
            using var bufferWriter = new FastBufferWriter(1024, Allocator.Temp);
            var buffer = new BufferSerializer<BufferSerializerWriter>(new BufferSerializerWriter(bufferWriter));
            (networkSerializable as INetworkSerializable)?.NetworkSerialize(buffer);
            var paramBytes = bufferWriter.ToArray();
            
            fastBufferWriter.WriteValueSafe(paramBytes.Length);
            fastBufferWriter.WriteBytes(paramBytes);
        }

        /// <summary>
        /// Reads a network serializable object from the provided FastBufferReader.
        /// </summary>
        /// <param name="fastBufferReader">The FastBufferReader to read from.</param>
        /// <param name="type">The Type of the serializable object.</param>
        /// <param name="serializable">The output parameter that will hold the deserialized object.</param>
        private static void ReadNetcodeSerializable(this FastBufferReader fastBufferReader, Type type,
            out object serializable)
        {
            fastBufferReader.ReadValueSafe(out int byteLength);
            var paramBytes = new byte[byteLength];
            fastBufferReader.ReadBytes(ref paramBytes, byteLength);

            using var bufferReader = new FastBufferReader(paramBytes, Allocator.Temp);
            var buffer = new BufferSerializer<BufferSerializerReader>(new BufferSerializerReader(bufferReader));
            serializable = Activator.CreateInstance(type);
            (serializable as INetworkSerializable)?.NetworkSerialize(buffer);
        }

        public static void WriteMethodInfoAndParameters(this FastBufferWriter fastBufferWriter, MethodBase methodInfo,
            object[] args)
        {
            fastBufferWriter.WriteValueSafe(methodInfo.Name);
            
            var parameters = methodInfo.GetParameters();
            fastBufferWriter.WriteValueSafe(parameters.Length);
            for (var i = 0; i < parameters.Length; i++)
            {
                var paramInfo = parameters[i];
                var paramInst = args[i];

                var isNull = paramInst == null;
                fastBufferWriter.WriteValueSafe(isNull);
                if (isNull) continue;

                if (paramInfo.ParameterType.GetInterfaces().Contains(typeof(INetworkSerializable)))
                    fastBufferWriter.WriteNetcodeSerializable(paramInst);
                else if (paramInfo.ParameterType.IsSerializable)
                    fastBufferWriter.WriteSystemSerializable(paramInst);
                else
                    throw new SerializationException(TextHandler.ObjectNotSerializable(paramInfo));
            }
        }

        private const BindingFlags BindingAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public static MethodInfo ReadMethodInfoAndParameters(this FastBufferReader fastBufferReader, 
            Type methodDeclaringType, ref object[] args)
        {
            fastBufferReader.ReadValueSafe(out string methodName);
            fastBufferReader.ReadValueSafe(out int paramCount);

            var method = methodDeclaringType.GetMethod(methodName, BindingAll);
            if (paramCount != method?.GetParameters().Length)
                throw new Exception(TextHandler.InconsistentParameterCount(method, paramCount));
            for (var i = 0; i < paramCount; i++)
            {
                fastBufferReader.ReadValueSafe(out bool isNull);
                if (isNull) continue;

                var paramInfo = method.GetParameters()[i];
                object serializable;
                
                if (paramInfo.ParameterType.GetInterfaces().Contains(typeof(INetworkSerializable)))
                    fastBufferReader.ReadNetcodeSerializable(paramInfo.ParameterType, out serializable);
                else if (paramInfo.ParameterType.IsSerializable)
                    fastBufferReader.ReadSystemSerializable(out serializable);
                else
                    throw new SerializationException(TextHandler.ObjectNotSerializable(paramInfo));
                args[i] = serializable;
            }

            return method;
        }
    }
}