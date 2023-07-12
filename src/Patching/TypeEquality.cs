#if NET35
extern alias References;

using Oxide.CSharp.Patching.Validation;
using References::Mono.Cecil;
using References::Mono.Cecil.Cil;
using References::Mono.Cecil.Rocks;
using System.Linq;

namespace Oxide.CSharp.Patching
{
    [HasName("mscorlib")]
    public class TypeEquality : TraversePatch
    {
        [HasName("System.Type", StringValidationType.Equals, System.StringComparison.InvariantCulture)]
        protected override bool OnTypeDefinition(TypeDefinition type)
        {
            bool changed = false;
            TypeDefinition boolean = type.Module.GetType("System", "Boolean");
            TypeDefinition compilerGenerated = type.Module.GetType("System.Runtime.CompilerServices", "CompilerGeneratedAttribute");
            MethodDefinition compilerGenMethod = compilerGenerated.GetConstructors().First();

            if (!DoesMethodExist("op_Equality", type))
            {
                MethodDefinition equality = new MethodDefinition("op_Equality", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName, boolean)
                {
                    DeclaringType = type
                };

                equality.Parameters.Add(new ParameterDefinition(type) { Name = "left" });
                equality.Parameters.Add(new ParameterDefinition(type) { Name = "right" });
                equality.CustomAttributes.Add(new CustomAttribute(compilerGenMethod));
                MethodBody body = new MethodBody(equality);
                equality.Body = body;
                type.Methods.Add(equality);
                body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
                body.Instructions.Add(Instruction.Create(OpCodes.Ceq));
                body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                body.OptimizeMacros();
                changed = true;
            }

            if (!DoesMethodExist("op_Inequality", type))
            {
                MethodDefinition inequality = new MethodDefinition("op_Inequality", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName, boolean)
                {
                    DeclaringType = type
                };

                inequality.Parameters.Add(new ParameterDefinition(type) { Name = "left" });
                inequality.Parameters.Add(new ParameterDefinition(type) { Name = "right" });
                inequality.CustomAttributes.Add(new CustomAttribute(compilerGenMethod));
                MethodBody body = new MethodBody(inequality);
                inequality.Body = body;
                type.Methods.Add(inequality);
                body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
                body.Instructions.Add(Instruction.Create(OpCodes.Ceq));
                body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
                body.Instructions.Add(Instruction.Create(OpCodes.Ceq));
                body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                body.OptimizeMacros();
                changed = true;
            }

            return changed;
        }

        private bool DoesMethodExist(string method, TypeDefinition type)
        {
            for (int i = 0; i < type.Methods.Count; i++)
            {
                MethodDefinition m = type.Methods[i];

                if (m.Name.Equals(method))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
