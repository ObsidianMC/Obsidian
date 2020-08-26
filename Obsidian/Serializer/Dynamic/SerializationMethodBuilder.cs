using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Serializer.Attributes;
using Obsidian.Util.Extensions;
using Obsidian.Serializer.Enums;

namespace Obsidian.Serializer.Dynamic
{
    internal static class SerializationMethodBuilder
    {
        private static readonly Dictionary<DataType, MethodInfo> writeMethods;
        private static readonly Dictionary<DataType, MethodInfo> readMethods;

        static SerializationMethodBuilder()
        {
            writeMethods = GetStreamMethods<WriteMethod>();
            readMethods = GetStreamMethods<ReadMethod>();
        }
        
        internal static MethodInfo BuildSerializationMethod<T>() where T : Packet
        {
            throw new NotImplementedException();
        }

        internal static PacketSerializer.DeserializeDelegate BuildDeserializationMethod<T>() where T : Packet
        {
            var type = typeof(T);
            var fields = GetFields(type).OrderBy((member) => member.Item2.Order);
            var streamMethods = typeof(MinecraftStream).GetMethods(PacketExtensions.Flags);

            var dynamicMethod = new DynamicMethod($"Deserialize{type.Name}",
                                                  MethodAttributes.Public | MethodAttributes.Static,
                                                  CallingConventions.Standard,
                                                  returnType: type,
                                                  parameterTypes: new[] { typeof(MinecraftStream) },
                                                  owner: type,
                                                  skipVisibility: true);
            var il = dynamicMethod.GetILGenerator();

            il.Emit(OpCodes.Newobj, type.GetConstructor(Array.Empty<Type>()));
            il.Emit(OpCodes.Dup);

            foreach (var (member, attribute) in fields)
            {
                if (member is FieldInfo field)
                {
                    DataType fieldType = attribute.Type != DataType.Auto ? attribute.Type : field.FieldType.ToDataType();
                    if (fieldType == DataType.Auto || !readMethods.TryGetValue(fieldType, out var readMethod))
                    {
                        throw new NotSupportedException();
                        continue;
                    }
                    
                    il.Emit(OpCodes.Ldarg_0);
                    var parameters = readMethod.GetParameters();
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var parameterType = parameters[i].ParameterType;
                        if (parameterType.IsByRef)
                            il.Emit(OpCodes.Ldnull);
                        else
                            il.Emit(OpCodes.Ldc_I4_0);
                    }
                    il.Emit(OpCodes.Callvirt, readMethod);

                    il.Emit(OpCodes.Stfld, field);
                    il.Emit(OpCodes.Dup);
                }
                else if (member is PropertyInfo property)
                {
                    var setMethod = property.SetMethod;
                    if (setMethod != null)
                    {
                        DataType propertyType = attribute.Type != DataType.Auto ? attribute.Type : property.PropertyType.ToDataType();
                        if (propertyType == DataType.Auto || !readMethods.TryGetValue(propertyType, out var readMethod))
                        {
                            throw new NotSupportedException();
                            continue;
                        }

                        il.Emit(OpCodes.Ldarg_0);
                        var parameters = readMethod.GetParameters();
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var parameterType = parameters[i].ParameterType;
                            if (parameterType.IsByRef)
                                il.Emit(OpCodes.Ldnull);
                            else
                                il.Emit(OpCodes.Ldc_I4_0);
                        }
                        il.Emit(OpCodes.Callvirt, readMethod);

                        il.Emit(OpCodes.Callvirt, setMethod);
                        il.Emit(OpCodes.Dup);
                    }
                }
            }

            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            return (PacketSerializer.DeserializeDelegate)dynamicMethod.CreateDelegate(typeof(PacketSerializer.DeserializeDelegate));
        }

        private static IEnumerable<(MemberInfo, FieldAttribute)> GetFields(Type type)
        {
            foreach (var member in type.GetMembers(PacketExtensions.Flags))
            {
                var attribute = member.GetCustomAttribute<FieldAttribute>();
                if (attribute != null)
                {
                    yield return (member, attribute);
                }
            }
        }

        private static Dictionary<DataType, MethodInfo> GetStreamMethods<T>() where T : Attribute, IStreamMethod
        {
            var dictionary = new Dictionary<DataType, MethodInfo>();
            var methods = typeof(MinecraftStream).GetMethods(PacketExtensions.Flags);
            for (int i = 0; i < methods.Length; i++)
            {
                var attribute = methods[i].GetCustomAttribute<T>();
                if (attribute != null)
                {
                    dictionary.TryAdd(attribute.Type, methods[i]);
                }
            }
            return dictionary;
        }
    }
}
