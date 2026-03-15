# dnlib Workflow

This document records the current generic `dnlib` patcher setup in this workspace and the exact steps to run it.

## Current state

The project is a generic patcher with an in-code configuration list.

- Target input DLL:
  - configured in [Program.cs](D:\homelab\turbotax\tools\DnlibFunctionPatcher\Program.cs)
- Patch list:
  - configured in [Program.cs](D:\homelab\turbotax\tools\DnlibFunctionPatcher\Program.cs)
- Output directory:
  - passed to the tool at runtime
- Output filename:
  - same as the input assembly filename
- Entry point:
  - [Invoke-DnlibPatch.ps1](D:\homelab\turbotax\Invoke-DnlibPatch.ps1)
- C# source:
  - [Program.cs](D:\homelab\turbotax\tools\DnlibFunctionPatcher\Program.cs)

What it does right now:

1. Loads the configured assembly with `dnlib`.
2. Prints basic metadata.
3. Applies each configured method patch.
4. Writes a new DLL to the output directory, using the original filename.

What it does not do yet:

1. It does not discover methods automatically.
2. It does not patch arbitrary IL sequences.
3. It relies on the patch list in code rather than a JSON file.

## How to run

From [D:\homelab\turbotax](D:\homelab\turbotax):

```powershell
.\Invoke-DnlibPatch.ps1 -OutputDirectory .\out
```

Expected output is similar to:

```text
Running hardcoded dnlib round-trip test
Loaded: D:\homelab\turbotax\samples\DemoTarget\bin\Debug\net10.0\DemoTarget.dll
Module: DemoTarget.dll
Types: ...
Methods: ...
Patched DemoTarget.FeatureFlags::get_IsUnitTest
Patched DemoTarget.ProductInfo::GetEditionName
Wrote patched assembly to D:\homelab\turbotax\out\DemoTarget.dll
```

## How configuration works

The patch list is defined directly in `Program.cs`:

```csharp
var config = new PatcherConfig
{
    TargetAssemblyPath = @"D:\path\to\YourAssembly.dll",
    Patches =
    {
        new MethodPatch(
            TypeName: "Your.Namespace.FeatureFlags",
            MethodName: "get_IsUnitTest",
            ParameterTypeNames: Array.Empty<string>(),
            Behavior: PatchBehavior.ReturnBoolean,
            Value: false),
        new MethodPatch(
            TypeName: "Your.Namespace.ProductInfo",
            MethodName: "GetEditionName",
            ParameterTypeNames: Array.Empty<string>(),
            Behavior: PatchBehavior.ReturnString,
            Value: "HOME_BUSINESS")
    }
};
```

For overloaded methods, populate `ParameterTypeNames` with full dnlib type names.

## Why the PowerShell wrapper exists

The wrapper sets local environment variables before running `dotnet`:

- `APPDATA`
- `DOTNET_CLI_HOME`
- `NUGET_PACKAGES`
- `DOTNET_SKIP_FIRST_TIME_EXPERIENCE`
- `DOTNET_CLI_TELEMETRY_OPTOUT`

This keeps restore/build artifacts under the workspace instead of depending on user-profile paths.

## Files involved

- [Invoke-DnlibPatch.ps1](D:\homelab\turbotax\Invoke-DnlibPatch.ps1)
  - Runs the project with workspace-local `.NET` and NuGet paths.
- [DnlibFunctionPatcher.csproj](D:\homelab\turbotax\tools\DnlibFunctionPatcher\DnlibFunctionPatcher.csproj)
  - References `dnlib` from NuGet.
- [Program.cs](D:\homelab\turbotax\tools\DnlibFunctionPatcher\Program.cs)
  - Generic patcher with an in-code patch list.
- [NuGet.Config](D:\homelab\turbotax\NuGet.Config)
  - NuGet source configuration.

## Supported patch behaviors

- `PatchBehavior.ReturnBoolean`
- `PatchBehavior.ReturnInt32`
- `PatchBehavior.ReturnString`
- `PatchBehavior.ReturnVoid`
- `PatchBehavior.ThrowNotSupported`

The patcher validates the method return type before rewriting the body.

## Constraints and notes

- This only applies to managed .NET assemblies.
- Strong-name signing or integrity checks may matter after patching.
- The patcher writes only to the output directory you provide.
- The target assembly path is hardcoded in `Program.cs` until you change it.

## Recommended next step

Modify [Program.cs](D:\homelab\turbotax\tools\DnlibFunctionPatcher\Program.cs) to point at your target assembly and desired method list, then run the wrapper with an output directory.
