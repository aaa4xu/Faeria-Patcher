using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.IO;

namespace FaeriaPatcher
{
    class Program
    {
        private static readonly string ORIGINAL_LOGIN_API_ADDRESS = "https://api.abrakam.com/v20170728/api/faeria/token/";
        private static readonly string GAME_SERVER_IP_FIELD = "DEFAULT_SERVER_IP";
        private static readonly string SERVERS_IP_FIELD = "GAME_SERVERS_IPS_JSON_URL";
        private static readonly string SERVERS_STATUS_FIELD = "SERVER_STATUS_JSON_URL";

        static void Main(string[] args)
        {
            Console.Title = "Faeria Patcher";

            if (args.Length != 2)
            {
                Console.WriteLine("FaeriaPatcher.dll GameDirectory ServerIP");
                return;
            }

            if (!Directory.Exists(args[0]))
            {
                Console.WriteLine($"Invalid Faeria install directory \"{args[0]}\"!");
                return;
            }

            var serverIp = args[1];
            var directoryPath = Path.Combine(args[0], @"Faeria_Data\Managed");
            var targetPath = Path.Combine(directoryPath, "Assembly-CSharp.dll");
            var originalPath = targetPath + ".original";

            if (!File.Exists(targetPath))
            {
                Console.WriteLine($"Failed to find Faeria assembly \"{targetPath}\"!");
                return;
            }

            if(!File.Exists(originalPath))
            {
                Console.WriteLine("Backing up original assembly...");
                File.Copy(targetPath, originalPath);
            }

            Directory.SetCurrentDirectory(directoryPath);

            var assembly = AssemblyDefinition.ReadAssembly(originalPath);
            ReplaceGameServerIp(assembly, serverIp);
            ReplaceStatusCheckAddress(assembly, serverIp);
            ReplaceLoginServerIp(assembly, serverIp);
            DisableTrafficEncription(assembly);
            assembly.Write(targetPath);

            Console.WriteLine("Done!");
        }

        private static string FormatServersIpsAddress(string serverIp)
        {
            return $"http://{serverIp}:8000/gameserver_ips.json";
        }

        private static string FormatServerStatusAddress(string serverIp)
        {
            return $"http://{serverIp}:8000/server_status.json";
        }

        private static string FormatTokenEndpointAddress(string serverIp)
        {
            return $"http://{serverIp}:8001/v20170728/api/faeria/token/";
        }

        private static void ReplaceGameServerIp(AssemblyDefinition assembly, string serverIp)
        {
            var type = GetType(assembly, "Abrakam", "WorldNetworkManager");
            ReplaceStaticStringField(type, GAME_SERVER_IP_FIELD, serverIp);

            Console.WriteLine("Patched game API address");
        }

        private static void ReplaceStatusCheckAddress(AssemblyDefinition assembly, string serverIp)
        {
            var type = GetType(assembly, "Abrakam", "ApplicationManager");
            ReplaceStaticStringField(type, SERVERS_IP_FIELD, FormatServersIpsAddress(serverIp));
            ReplaceStaticStringField(type, SERVERS_STATUS_FIELD, FormatServerStatusAddress(serverIp));

            Console.WriteLine("Patched status API address");
        }

        private static void DisableTrafficEncription(AssemblyDefinition assembly)
        {
            var type = GetType(assembly, "Abrakam", "NetworkManager");

            GetMethod(type, "SendEncryptedCommand").Body = GetMethod(type, "SendCommand").Body;
            GetMethod(type, "SendNonSequencedEncryptedCommand").Body = GetMethod(type, "SendNonSequencedCommand").Body;

            Console.WriteLine("Disabled network traffic encription");
        }

        private static void ReplaceLoginServerIp(AssemblyDefinition assembly, string serverIp)
        {
            var authManagerType = GetType(assembly, "Abrakam", "AuthenticationManager");

            foreach (var type in authManagerType.NestedTypes)
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;

                    for (int i = 0; i < method.Body.Instructions.Count - 1; i++)
                    {
                        var instruction = method.Body.Instructions[i];

                        if (instruction.Operand != null && instruction.Operand.ToString().Equals(ORIGINAL_LOGIN_API_ADDRESS))
                        {
                            instruction.Operand = FormatTokenEndpointAddress(serverIp);
                        }
                    }
                }
            }

            Console.WriteLine("Patched auth API address");
        }

        private static void ReplaceStaticStringField(TypeDefinition type, string fieldName, string value)
        {
            foreach (var method in type.Methods)
            {
                if (!method.Name.Equals(".cctor")) continue;

                for (int i = 0; i < method.Body.Instructions.Count - 1; i++)
                {
                    var instruction = method.Body.Instructions[i];

                    if (instruction.OpCode == OpCodes.Ldstr && instruction.Next.OpCode == OpCodes.Stsfld && ((FieldDefinition)instruction.Next.Operand).Name.Equals(fieldName))
                    {
                        instruction.Operand = value;
                    }
                }
            }
        }

        private static TypeDefinition GetType(AssemblyDefinition assembly, string typeNamespace, string typeName)
        {
            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.Types)
                {
                    if (type.Namespace.Equals(typeNamespace) && type.Name.Equals(typeName))
                    {
                        return type;
                    }
                }
            }

            throw new Exception($"{typeNamespace}.{typeName} not found in assembly!");
        }

        private static MethodDefinition GetMethod(TypeDefinition type, string methodName)
        {
            foreach (var method in type.Methods)
            {
                if(method.Name.Equals(methodName))
                {
                    return method;
                }
            }

            throw new Exception($"Method {methodName} not found in type!");
        }
    }
}
