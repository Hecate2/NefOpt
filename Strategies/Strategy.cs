﻿using Neo.Json;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.VM;
using System.Reflection;

namespace Neo.Optimizer
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class StrategyAttribute : Attribute
    {
        public string? Name { get; init; }
        public int Priority = 0;  // greater num to be executed first
    }

    public class Optimizer
    {
        public static int[] OperandSizePrefixTable = new int[256];
        public static int[] OperandSizeTable = new int[256];

        public static Dictionary<string, Func<NefFile, ContractManifest, JToken, (NefFile nef, ContractManifest manifest, JToken debugInfo)>> strategies = new();
        static Optimizer()
        {
            var assembly = Assembly.GetExecutingAssembly();
            foreach (Type type in assembly.GetTypes())
                RegisterStrategies(type);
            foreach (FieldInfo field in typeof(OpCode).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                OperandSizeAttribute? attribute = field.GetCustomAttribute<OperandSizeAttribute>();
                if (attribute == null) continue;
                int index = (int)(OpCode)field.GetValue(null)!;
                OperandSizePrefixTable[index] = attribute.SizePrefix;
                OperandSizeTable[index] = attribute.Size;
            }
        }
        public static void RegisterStrategies(Type type)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                StrategyAttribute attribute = method.GetCustomAttribute<StrategyAttribute>()!;
                if (attribute is null) continue;
                string name = string.IsNullOrEmpty(attribute.Name) ? method.Name.ToLowerInvariant() : attribute.Name;
                strategies[name] = method.CreateDelegate<Func<NefFile, ContractManifest, JToken, (NefFile nef, ContractManifest manifest, JToken debugInfo)>>();
            }
        }
    }
}
