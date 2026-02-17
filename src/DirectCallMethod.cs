extern alias References;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oxide.Core.Plugins;
using Oxide.CSharp.Common;
using Oxide.Pooling;
using References::Mono.Cecil;
using References::Mono.Cecil.Cil;
using References::Mono.Cecil.Rocks;

namespace Oxide.Core.CSharp
{
    public class DirectCallMethod
    {
        private readonly ModuleDefinition _module;
        private readonly TypeDefinition _type;
        private readonly MethodDefinition _method;
        private readonly MethodBody _body;
        private readonly Instruction _endInstruction;
        private readonly MethodReference _getLength;
        private readonly MethodReference _getChars;
        private readonly MethodReference _isNullOrEmpty;
        private readonly MethodReference _stringEquals;
        private readonly string _hookAttribute;
        private readonly Dictionary<Instruction, Node> _jumpToEdgePlaceholderTargets = new();
        private readonly List<Instruction> _jumpToEndPlaceholders = new();
        private readonly Dictionary<string, MethodDefinition> _hookMethods = new();

        public DirectCallMethod(ModuleDefinition module, TypeDefinition type, AssemblyDefinition baseAssembly)
        {
            _module = module;
            _type = type;

            _getLength = module.ImportReference(typeof(string).GetMethod("get_Length",
                Constants.StringGetLengthTypeArray));

            _getChars = module.ImportReference(typeof(string).GetMethod("get_Chars",
                Constants.StringGetCharsTypeArray));

            _isNullOrEmpty = module.ImportReference(typeof(string).GetMethod("IsNullOrEmpty",
                Constants.StringIsNullOrEmptyTypeArray));

            _stringEquals = module.ImportReference(typeof(string).GetMethod("Equals",
                Constants.StringEqualsTypeArray));

            _hookAttribute = typeof(HookMethodAttribute).FullName;

            // Copy method definition from base class
            ModuleDefinition baseModule = baseAssembly.MainModule;
            TypeDefinition baseType = module.ImportReference(baseAssembly.MainModule.GetType("Oxide.Plugins.CSharpPlugin")).Resolve();
            MethodDefinition baseMethod = module.ImportReference(baseType.Methods.First(method => method.Name == "DirectCallHook")).Resolve();

            // Create method override based on virtual method signature
            _method = new MethodDefinition(baseMethod.Name, baseMethod.Attributes,
                baseModule.ImportReference(baseMethod.ReturnType))
            {
                DeclaringType = type
            };

            int methodParameterCount = baseMethod.Parameters.Count;
            for (int i = 0; i < methodParameterCount; i++)
            {
                ParameterDefinition parameter = baseMethod.Parameters[i];
                ParameterDefinition newParam = new(parameter.Name, parameter.Attributes,
                    module.ImportReference(parameter.ParameterType))
                {
                    IsOut = parameter.IsOut,
                    Constant = parameter.Constant,
                    MarshalInfo = parameter.MarshalInfo,
                    IsReturnValue = parameter.IsReturnValue
                };

                int parameterCustomAttributeCount = parameter.CustomAttributes.Count;
                for (int j = 0; j < parameterCustomAttributeCount; j++)
                {
                    CustomAttribute attribute = parameter.CustomAttributes[j];
                    newParam.CustomAttributes.Add(new CustomAttribute(module.ImportReference(attribute.Constructor)));
                }

                _method.Parameters.Add(newParam);
            }

            int methodCustomAttributeCount = baseMethod.CustomAttributes.Count;
            for (int i = 0; i < methodCustomAttributeCount; i++)
            {
                CustomAttribute attribute = baseMethod.CustomAttributes[i];
                _method.CustomAttributes.Add(new CustomAttribute(module.ImportReference(attribute.Constructor)));
            }

            _method.ImplAttributes = baseMethod.ImplAttributes;
            _method.SemanticsAttributes = baseMethod.SemanticsAttributes;

            // Replace the NewSlot attribute with ReuseSlot
            _method.Attributes &= ~MethodAttributes.NewSlot;
            _method.Attributes |= MethodAttributes.ReuseSlot;

            // Create new method body
            _body = new MethodBody(_method);
            _body.SimplifyMacros();
            _method.Body = _body;
            type.Methods.Add(_method);

            // Create variables
            _body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
            _body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));

            // Initialize return value to null
            AddInstruction(OpCodes.Ldarg_2);
            AddInstruction(OpCodes.Ldnull);
            AddInstruction(OpCodes.Stind_Ref);

            // Check for name null or empty
            AddInstruction(OpCodes.Ldarg_1);
            AddInstruction(OpCodes.Call, _isNullOrEmpty);
            Instruction empty = AddInstruction(OpCodes.Brfalse, _body.Instructions[0]);
            Return(false);

            // Get method name length
            empty.Operand = AddInstruction(OpCodes.Ldarg_1);
            AddInstruction(OpCodes.Callvirt, _getLength);
            AddInstruction(OpCodes.Stloc_0);

            // Initialize i counter variable to 0
            AddInstruction(OpCodes.Ldc_I4_0);
            AddInstruction(OpCodes.Stloc_1);

            // Find all hook methods defined by the plugin
            int methodCount = type.Methods.Count;
            for (int i = 0; i < methodCount; i++)
            {
                MethodDefinition method = type.Methods[i];
                if (method.IsStatic || method.DeclaringType != type || method.IsGetter || method.IsSetter)
                {
                    continue;
                }

                if (!method.IsPrivate && !IsHookMethod(method))
                {
                    continue;
                }

                if (method.HasGenericParameters || method.ReturnType.IsGenericParameter)
                {
                    continue;
                }

                // Ignore compiler-generated
                if (method.Name.IndexOf('<') >= 0)
                {
                    continue;
                }

                string name;
                int parameterCount = method.Parameters.Count;
                if (parameterCount == 0)
                {
                    name = method.Name;
                }
                else
                {
                    StringBuilder stringBuilder = PoolFactory<StringBuilder>.Shared.Take();
                    try
                    {
                        stringBuilder.Append(method.Name);
                        stringBuilder.Append('(');

                        for (int j = 0; j < parameterCount; j++)
                        {
                            if (j > 0)
                            {
                                stringBuilder.Append(',');
                                stringBuilder.Append(' ');
                            }

                            AppendFormattedTypeName(stringBuilder, method.Parameters[j].ParameterType);
                        }

                        stringBuilder.Append(')');
                        name = stringBuilder.ToString();
                    }
                    finally
                    {
                        stringBuilder.Length = 0;
                        PoolFactory<StringBuilder>.Shared.Return(stringBuilder);
                    }
                }

                _hookMethods[name] = method;
            }

            // Build a hook method name trie
            Node rootNode = new();
            foreach (string methodName in _hookMethods.Keys)
            {
                Node currentNode = rootNode;
                int methodNameLength = methodName.Length;
                for (int i = 1; i <= methodNameLength; i++)
                {
                    char letter = methodName[i - 1];
                    if (!currentNode.Edges.TryGetValue(letter, out Node nextNode))
                    {
                        nextNode = new Node
                        {
                            Parent = currentNode,
                            Char = letter
                        };

                        currentNode.Edges[letter] = nextNode;
                    }

                    if (i == methodNameLength)
                    {
                        nextNode.Name = methodName;
                    }

                    currentNode = nextNode;
                }
            }

            // Build conditional method call logic from trie nodes
            int n = 1;
            foreach (char edge in rootNode.Edges.Keys)
            {
                BuildNode(rootNode.Edges[edge], n++);
            }

            // No valid method was found
            _endInstruction = Return(false);

            foreach (Instruction instruction in _jumpToEdgePlaceholderTargets.Keys)
            {
                instruction.Operand = _jumpToEdgePlaceholderTargets[instruction].FirstInstruction;
            }

            int placeholderCount = _jumpToEndPlaceholders.Count;
            for (int i = 0; i < placeholderCount; i++)
            {
                Instruction instruction = _jumpToEndPlaceholders[i];
                instruction.Operand = _endInstruction;
            }

            _body.OptimizeMacros();
        }

        private bool IsHookMethod(MethodDefinition method)
        {
            int customAttributeCount = method.CustomAttributes.Count;
            for (int i = 0; i < customAttributeCount; i++)
            {
                CustomAttribute attribute = method.CustomAttributes[i];
                if (attribute.AttributeType.FullName != _hookAttribute)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private void BuildNode(Node node, int edgeNumber)
        {
            // Check the char index lower than length on first edge
            if (edgeNumber == 1)
            {
                node.FirstInstruction = AddInstruction(OpCodes.Ldloc_1);
                AddInstruction(OpCodes.Ldloc_0);
                _jumpToEndPlaceholders.Add(AddInstruction(OpCodes.Bge, _body.Instructions[0]));
            }

            // Check the char at the current position
            if (edgeNumber == 1)
            {
                AddInstruction(OpCodes.Ldarg_1); //method_name
            }
            else
            {
                node.FirstInstruction = AddInstruction(OpCodes.Ldarg_1);
            }

            AddInstruction(OpCodes.Ldloc_1);                            // i
            AddInstruction(OpCodes.Callvirt, _getChars);                 // method_name[i]
            AddInstruction(Ldc_I4_n(node.Char));

            if (node.Parent.Edges.Count > edgeNumber)
            {
                // If char does not match and there are more edges to check
                JumpToEdge(node.Parent.Edges.Values.ElementAt(edgeNumber));
            }
            else
            {
                // If char does not match and there are no more edges to check
                JumpToEnd();
            }

            if (node.Edges.Count == 1 && node.Name == null)
            {
                Node lastEdge = node;
                while (lastEdge.Edges.Count == 1 && lastEdge.Name == null)
                {
                    lastEdge = lastEdge.Edges.Values.First();
                }

                if (lastEdge.Edges.Count == 0 && lastEdge.Name != null)
                {
                    // There is only one remaining possible hook on this path
                    AddInstruction(OpCodes.Ldarg_1);
                    AddInstruction(Instruction.Create(OpCodes.Ldstr, lastEdge.Name));
                    AddInstruction(OpCodes.Callvirt, _stringEquals);
                    // If the full method name does not match the only remaining possible hook, return false
                    _jumpToEndPlaceholders.Add(AddInstruction(OpCodes.Brfalse, _body.Instructions[0]));

                    // Method has been found
                    CallMethod(_hookMethods[lastEdge.Name]);
                    Return(true);

                    return;
                }
            }

            // Method continuing with this char exists, increment position
            AddInstruction(OpCodes.Ldloc_1);
            AddInstruction(OpCodes.Ldc_I4_1);
            AddInstruction(OpCodes.Add);
            AddInstruction(OpCodes.Stloc_1);

            if (node.Name != null)
            {
                // Check if we are at the end of the method name
                AddInstruction(OpCodes.Ldloc_1);
                AddInstruction(OpCodes.Ldloc_0);
                // If the method name is longer than the current position
                if (node.Edges.Count > 0)
                {
                    JumpToEdge(node.Edges.Values.First());
                }
                else
                {
                    JumpToEnd();
                }

                // Method has been found
                CallMethod(_hookMethods[node.Name]);
                Return(true);
            }

            int n = 1;
            foreach (char edge in node.Edges.Keys)
            {
                BuildNode(node.Edges[edge], n++);
            }
        }

        private void CallMethod(MethodDefinition method)
        {
            Dictionary<ParameterDefinition, VariableDefinition> paramDict = new();
            // check for ref/out param
            int methodParameterCount = method.Parameters.Count;
            for (int i = 0; i < methodParameterCount; i++)
            {
                ParameterDefinition parameter = method.Parameters[i];
                if (parameter.ParameterType is not ByReferenceType byReferenceType)
                {
                    continue;
                }

                VariableDefinition refParam = AddVariable(_module.ImportReference(byReferenceType.ElementType));
                AddInstruction(OpCodes.Ldarg_3);    // object[] params
                AddInstruction(Ldc_I4_n(i));        // param_number
                AddInstruction(OpCodes.Ldelem_Ref);
                AddInstruction(OpCodes.Unbox_Any, _module.ImportReference(byReferenceType.ElementType));
                AddInstruction(OpCodes.Stloc_S, refParam);
                paramDict[parameter] = refParam;
            }

            if (method.ReturnType.Name != "Void")
            {
                AddInstruction(OpCodes.Ldarg_2); // out object ret
            }

            AddInstruction(OpCodes.Ldarg_0);    // this
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                ParameterDefinition parameter = method.Parameters[i];
                if (parameter.ParameterType is ByReferenceType)
                {
                    AddInstruction(OpCodes.Ldloca, paramDict[parameter]);
                    continue;
                }

                // TODO: Handle params array?
                AddInstruction(OpCodes.Ldarg_3);    // object[] params
                AddInstruction(Ldc_I4_n(i));        // param_number
                AddInstruction(OpCodes.Ldelem_Ref);
                AddInstruction(OpCodes.Unbox_Any, _module.ImportReference(parameter.ParameterType));
            }

            AddInstruction(OpCodes.Call, _module.ImportReference(method));

            // handle ref/out params
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                ParameterDefinition parameter = method.Parameters[i];
                if (parameter.ParameterType is not ByReferenceType byReferenceType)
                {
                    continue;
                }

                AddInstruction(OpCodes.Ldarg_3);    // object[] params
                AddInstruction(Ldc_I4_n(i));        // param_number
                AddInstruction(OpCodes.Ldloc_S, paramDict[parameter]);
                AddInstruction(OpCodes.Box, _module.ImportReference(byReferenceType.ElementType));
                AddInstruction(OpCodes.Stelem_Ref);
            }

            if (method.ReturnType.Name == "Void")
            {
                return;
            }

            if (method.ReturnType.Name != "Object")
            {
                AddInstruction(OpCodes.Box, _module.ImportReference(method.ReturnType));
            }

            AddInstruction(OpCodes.Stind_Ref);
        }

        private Instruction Return(bool value)
        {
            Instruction instruction = AddInstruction(Ldc_I4_n(value ? 1 : 0));
            AddInstruction(OpCodes.Ret);
            return instruction;
        }

        private void JumpToEdge(Node node)
        {
            Instruction instruction = AddInstruction(OpCodes.Bne_Un, _body.Instructions[1]);
            _jumpToEdgePlaceholderTargets[instruction] = node;
        }

        private void JumpToEnd() => _jumpToEndPlaceholders.Add(AddInstruction(OpCodes.Bne_Un, _body.Instructions[0]));

        private Instruction AddInstruction(OpCode opCode) => AddInstruction(Instruction.Create(opCode));

        private Instruction AddInstruction(OpCode opCode, Instruction instruction) =>
            AddInstruction(Instruction.Create(opCode, instruction));

        private Instruction AddInstruction(OpCode opCode, MethodReference methodReference) =>
            AddInstruction(Instruction.Create(opCode, methodReference));

        private Instruction AddInstruction(OpCode opCode, TypeReference typeReference) =>
            AddInstruction(Instruction.Create(opCode, typeReference));

        private Instruction AddInstruction(OpCode opCode, int value) =>
            AddInstruction(Instruction.Create(opCode, value));

        private Instruction AddInstruction(OpCode opCode, VariableDefinition value) =>
            AddInstruction(Instruction.Create(opCode, value));

        private Instruction AddInstruction(Instruction instruction)
        {
            _body.Instructions.Add(instruction);
            return instruction;
        }

        private VariableDefinition AddVariable(TypeReference typeReference)
        {
            VariableDefinition variableDefinition = new(typeReference);
            _body.Variables.Add(variableDefinition);
            return variableDefinition;
        }

        private Instruction Ldc_I4_n(int value) => value switch
        {
            0 => Instruction.Create(OpCodes.Ldc_I4_0),
            1 => Instruction.Create(OpCodes.Ldc_I4_1),
            2 => Instruction.Create(OpCodes.Ldc_I4_2),
            3 => Instruction.Create(OpCodes.Ldc_I4_3),
            4 => Instruction.Create(OpCodes.Ldc_I4_4),
            5 => Instruction.Create(OpCodes.Ldc_I4_5),
            6 => Instruction.Create(OpCodes.Ldc_I4_6),
            7 => Instruction.Create(OpCodes.Ldc_I4_7),
            8 => Instruction.Create(OpCodes.Ldc_I4_8),
            _ => Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)value)
        };

        private void AppendFormattedTypeName(StringBuilder stringBuilder, TypeReference type)
        {
            string fullName = type.FullName;
            int fullNameLength = fullName.Length;
            for (int i = 0; i < fullNameLength; i++)
            {
                char character = fullName[i];
                switch (character)
                {
                    case '/':
                    {
                        stringBuilder.Append('+');
                        break;
                    }
                    case '<':
                    {
                        stringBuilder.Append('[');
                        break;
                    }
                    case '>':
                    {
                        stringBuilder.Append(']');
                        break;
                    }
                    default:
                    {
                        stringBuilder.Append(character);
                        break;
                    }
                }
            }
        }
    }

    public class Node
    {
        public char Char;
        public string Name;
        public readonly Dictionary<char, Node> Edges = new();
        public Node Parent;
        public Instruction FirstInstruction;
    }
}
