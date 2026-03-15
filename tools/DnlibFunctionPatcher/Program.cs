using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

var config = new PatcherConfig
{
    TargetAssemblyPath = @"C:\Program Files\TurboTax\Individual 2025\64bit\Intuit.Ctg.Wte.Service.dll",
    Patches =
    {
        // Force activation check to always say not required
        new MethodPatch(
            TypeName: "Intuit.Ctg.Wte.Service.ProductConfiguration.ProductConfigurationService",
            MethodName: "IsProductActivationRequired",
            ParameterTypeNames: Array.Empty<string>(),
            Behavior: PatchBehavior.ReturnBoolean,
            Value: false),

        // Force persisted edition to HomeAndBusiness (enum value 32)
        new MethodPatch(
            TypeName: "Intuit.Ctg.Wte.Service.Entitlement.EntitlementService",
            MethodName: "get_PersistedEntitledEdition",
            ParameterTypeNames: Array.Empty<string>(),
            Behavior: PatchBehavior.ReturnEnumInt32,
            Value: 32),

        // Force all states to be entitled
        new MethodPatch(
            TypeName: "Intuit.Ctg.Wte.Service.Entitlement.EntitlementService",
            MethodName: "IsStateEntitled",
            ParameterTypeNames: new[] { "System.String" },
            Behavior: PatchBehavior.ReturnBoolean,
            Value: true),

        // Return a large number of available free states
        new MethodPatch(
            TypeName: "Intuit.Ctg.Wte.Service.Entitlement.EntitlementService",
            MethodName: "GetAvailableFreeStates",
            ParameterTypeNames: Array.Empty<string>(),
            Behavior: PatchBehavior.ReturnInt32,
            Value: 99),
    }
};

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: DnlibFunctionPatcher <output-directory>");
    return 1;
}

var outputDirectory = Path.GetFullPath(args[0]);
var targetAssemblyPath = Path.GetFullPath(config.TargetAssemblyPath);
var outputAssemblyPath = Path.Combine(outputDirectory, Path.GetFileName(targetAssemblyPath));

if (!File.Exists(targetAssemblyPath))
{
    Console.Error.WriteLine($"Target assembly not found: {targetAssemblyPath}");
    return 1;
}

var moduleContext = ModuleDef.CreateModuleContext();
using var module = ModuleDefMD.Load(targetAssemblyPath, moduleContext);

Console.WriteLine($"Loaded: {targetAssemblyPath}");
Console.WriteLine($"Module: {module.Name}");

var allTypes = module.GetTypes().ToList();
Console.WriteLine($"Types:   {allTypes.Count}");
Console.WriteLine($"Methods: {allTypes.Sum(t => t.Methods.Count)}");
Console.WriteLine();

InspectModule(module);

foreach (var patch in config.Patches)
{
    ApplyPatch(module, patch);
    Console.WriteLine($"Patched {patch.TypeName}::{patch.MethodName}");
}

Directory.CreateDirectory(outputDirectory);

var writerOptions = new ModuleWriterOptions(module)
{
    Logger = DummyLogger.NoThrowInstance
};

module.Write(outputAssemblyPath, writerOptions);

Console.WriteLine($"Wrote patched assembly to {outputAssemblyPath}");
return 0;

static void InspectModule(ModuleDef module)
{
    Console.WriteLine("=== Type / Method Inspection ===");
    foreach (var type in module.GetTypes().OrderBy(t => t.FullName))
    {
        if (type.IsGlobalModuleType) continue;

        var access = type.IsPublic ? "public" : type.IsNotPublic ? "internal" : "nested";
        var kind   = type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : "class";
        Console.WriteLine($"  [{access}] {kind} {type.FullName}");

        foreach (var method in type.Methods.OrderBy(m => m.Name))
        {
            var mAccess = method.IsPublic  ? "public"
                        : method.IsFamily  ? "protected"
                        : method.IsAssembly ? "internal"
                        : method.IsFamilyOrAssembly ? "protected internal"
                        : "private";
            var mKind = method.IsStatic ? "static " : "";
            var ret   = method.ReturnType.FullName;
            var parms = string.Join(", ", method.Parameters
                            .Where(p => !p.IsHiddenThisParameter)
                            .Select(p => $"{p.Type.FullName} {p.Name}"));
            var hasBody = method.HasBody ? $"  [{method.Body.Instructions.Count} IL ops]" : "  [no body]";
            Console.WriteLine($"      {mAccess} {mKind}{ret} {method.Name}({parms}){hasBody}");
        }
        Console.WriteLine();
    }
    Console.WriteLine("=== End Inspection ===");
    Console.WriteLine();
}

static void ApplyPatch(ModuleDef module, MethodPatch patch)
{
    var type = module.Find(patch.TypeName, isReflectionName: false)
        ?? throw new InvalidOperationException($"Type not found: {patch.TypeName}");

    var candidates = type.Methods
        .Where(method => string.Equals(method.Name, patch.MethodName, StringComparison.Ordinal))
        .ToList();

    if (patch.ParameterTypeNames.Count > 0)
    {
        candidates = candidates.Where(method => ParametersMatch(method, patch.ParameterTypeNames)).ToList();
    }

    if (candidates.Count == 0)
    {
        throw new InvalidOperationException($"Method not found: {patch.TypeName}::{patch.MethodName}");
    }

    if (candidates.Count > 1)
    {
        throw new InvalidOperationException(
            $"Method is ambiguous: {patch.TypeName}::{patch.MethodName}. Add parameter types to disambiguate.");
    }

    var method = candidates[0];
    method.Body = new CilBody
    {
        InitLocals = false,
        MaxStack = 8
    };

    foreach (var instruction in BuildInstructions(module, method, patch))
    {
        method.Body.Instructions.Add(instruction);
    }

    method.Body.SimplifyBranches();
    method.Body.OptimizeBranches();
    method.Body.OptimizeMacros();
}

static List<Instruction> BuildInstructions(ModuleDef module, MethodDef method, MethodPatch patch)
{
    return patch.Behavior switch
    {
        PatchBehavior.ReturnBoolean => BuildReturnBooleanInstructions(method, patch),
        PatchBehavior.ReturnInt32 => BuildReturnInt32Instructions(method, patch),
        PatchBehavior.ReturnEnumInt32 => BuildReturnEnumInt32Instructions(patch),
        PatchBehavior.ReturnString => BuildReturnStringInstructions(method, patch),
        PatchBehavior.ReturnVoid => BuildReturnVoidInstructions(method),
        PatchBehavior.ThrowNotSupported => BuildThrowInstructions(module, patch),
        _ => throw new InvalidOperationException($"Unsupported behavior: {patch.Behavior}")
    };
}

static List<Instruction> BuildReturnEnumInt32Instructions(MethodPatch patch)
{
    if (patch.Value is not int enumValue)
        throw new InvalidOperationException($"Patch {patch.TypeName}::{patch.MethodName} requires an int value for ReturnEnumInt32.");

    return
    [
        Instruction.CreateLdcI4(enumValue),
        Instruction.Create(OpCodes.Ret)
    ];
}

static List<Instruction> BuildReturnBooleanInstructions(MethodDef method, MethodPatch patch)
{
    EnsureReturnType(method, "System.Boolean");

    if (patch.Value is not bool boolValue)
    {
        throw new InvalidOperationException($"Patch {patch.TypeName}::{patch.MethodName} requires a boolean value.");
    }

    return
    [
        Instruction.Create(boolValue ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0),
        Instruction.Create(OpCodes.Ret)
    ];
}

static List<Instruction> BuildReturnInt32Instructions(MethodDef method, MethodPatch patch)
{
    EnsureReturnType(method, "System.Int32");

    if (patch.Value is not int intValue)
    {
        throw new InvalidOperationException($"Patch {patch.TypeName}::{patch.MethodName} requires an Int32 value.");
    }

    return
    [
        Instruction.CreateLdcI4(intValue),
        Instruction.Create(OpCodes.Ret)
    ];
}

static List<Instruction> BuildReturnStringInstructions(MethodDef method, MethodPatch patch)
{
    EnsureReturnType(method, "System.String");

    if (patch.Value is not string stringValue)
    {
        throw new InvalidOperationException($"Patch {patch.TypeName}::{patch.MethodName} requires a string value.");
    }

    return
    [
        Instruction.Create(OpCodes.Ldstr, stringValue),
        Instruction.Create(OpCodes.Ret)
    ];
}

static List<Instruction> BuildReturnVoidInstructions(MethodDef method)
{
    if (method.ReturnType.ElementType != ElementType.Void)
    {
        throw new InvalidOperationException($"Method {method.FullName} does not return void.");
    }

    return
    [
        Instruction.Create(OpCodes.Ret)
    ];
}

static List<Instruction> BuildThrowInstructions(ModuleDef module, MethodPatch patch)
{
    var exceptionCtor = module.Import(
        typeof(NotSupportedException).GetConstructor(new[] { typeof(string) })
        ?? throw new InvalidOperationException("Unable to resolve NotSupportedException constructor."));

    var message = patch.Value as string ?? $"Patched method {patch.TypeName}::{patch.MethodName} was blocked.";

    return
    [
        Instruction.Create(OpCodes.Ldstr, message),
        Instruction.Create(OpCodes.Newobj, exceptionCtor),
        Instruction.Create(OpCodes.Throw)
    ];
}

static void EnsureReturnType(MethodDef method, string expectedTypeName)
{
    if (!string.Equals(method.ReturnType.FullName, expectedTypeName, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Method {method.FullName} returns {method.ReturnType.FullName}, expected {expectedTypeName}.");
    }
}

static bool ParametersMatch(MethodDef method, IReadOnlyList<string> parameterTypeNames)
{
    if (method.MethodSig is null || method.MethodSig.Params.Count != parameterTypeNames.Count)
    {
        return false;
    }

    for (var index = 0; index < parameterTypeNames.Count; index++)
    {
        if (!string.Equals(method.MethodSig.Params[index].FullName, parameterTypeNames[index], StringComparison.Ordinal))
        {
            return false;
        }
    }

    return true;
}

internal sealed class PatcherConfig
{
    public string TargetAssemblyPath { get; init; } = string.Empty;
    public List<MethodPatch> Patches { get; init; } = new();
}

internal sealed record MethodPatch(
    string TypeName,
    string MethodName,
    IReadOnlyList<string> ParameterTypeNames,
    PatchBehavior Behavior,
    object? Value);

internal enum PatchBehavior
{
    ReturnBoolean,
    ReturnInt32,
    ReturnEnumInt32,
    ReturnString,
    ReturnVoid,
    ThrowNotSupported
}
