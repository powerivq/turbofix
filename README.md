# dnlib Function Patcher

Patches TurboTax 2025 to remove activation and entitlement checks. Already fully configured — just install the prerequisite and run.

---

## Prerequisite — .NET 10 SDK

This is the only thing you need to install.

1. Go to: https://dotnet.microsoft.com/en-us/download/dotnet/10.0
2. Under **SDK**, download the **Windows x64** installer and run it.
3. Verify it worked — open PowerShell and run:

```powershell
dotnet --version
```

It should print `10.0.xxx`. If it does, you're ready.

> **PowerShell** is built into Windows — search for it in the Start menu.

---

## Running

Open PowerShell, navigate to this folder, and run:

```powershell
.\Invoke-DnlibPatch.ps1
```

The first run takes 10–30 seconds to compile. Subsequent runs are fast.

You will see output like:

```
Running dnlib patcher
Loaded: C:\Program Files\TurboTax\Individual 2025\64bit\Intuit.Ctg.Wte.Service.dll
Module: Intuit.Ctg.Wte.Service.dll
Types:   1828
Methods: 15899

Patched ...ProductConfigurationService::IsProductActivationRequired
Patched ...EntitlementService::get_PersistedEntitledEdition
Patched ...EntitlementService::IsStateEntitled
Patched ...EntitlementService::GetAvailableFreeStates
Wrote patched assembly to .\out\Intuit.Ctg.Wte.Service.dll
```

---

## Applying the patch

The modified DLL is written to `.\out\`. Copy it over the original:

```powershell
Copy-Item ".\out\Intuit.Ctg.Wte.Service.dll" "C:\Program Files\TurboTax\Individual 2025\64bit\Intuit.Ctg.Wte.Service.dll"
```

> **Back up the original first:**
> ```powershell
> Copy-Item "C:\Program Files\TurboTax\Individual 2025\64bit\Intuit.Ctg.Wte.Service.dll" `
>           "C:\Program Files\TurboTax\Individual 2025\64bit\Intuit.Ctg.Wte.Service.dll.bak"
> ```

---

## What gets patched

All patches target `Intuit.Ctg.Wte.Service.dll`:

| Method | Change |
|---|---|
| `ProductConfigurationService::IsProductActivationRequired` | Always returns `false` |
| `EntitlementService::get_PersistedEntitledEdition` | Always returns `HomeAndBusiness` |
| `EntitlementService::IsStateEntitled(string)` | Always returns `true` |
| `EntitlementService::GetAvailableFreeStates()` | Always returns `99` |

---

## Customizing patches

All configuration is at the top of [`tools/DnlibFunctionPatcher/Program.cs`](tools/DnlibFunctionPatcher/Program.cs). Each patch entry looks like:

```csharp
new MethodPatch(
    TypeName: "Full.Namespace.ClassName",       // from a decompiler (ILSpy / dnSpy)
    MethodName: "MethodName",
    ParameterTypeNames: Array.Empty<string>(),  // empty if no parameters
    Behavior: PatchBehavior.ReturnBoolean,
    Value: true),
```

### Behaviors

| `PatchBehavior` | `Value` | Effect |
|---|---|---|
| `ReturnBoolean` | `true`/`false` | Always returns that bool |
| `ReturnInt32` | integer | Always returns that int |
| `ReturnEnumInt32` | integer | Returns an enum by its raw integer value |
| `ReturnString` | `"text"` | Always returns that string |
| `ReturnVoid` | *(omit)* | Returns immediately |
| `ThrowNotSupported` | `"message"` | Always throws `NotSupportedException` |

### Finding method names

Use [ILSpy](https://github.com/icsharpcode/ILSpy/releases) or [dnSpy](https://github.com/dnSpy/dnSpy/releases) to browse any `.dll`. Alternatively, run the patcher and pipe the output to a file — it prints every type and method in the target assembly:

```powershell
.\Invoke-DnlibPatch.ps1 2>&1 | Tee-Object -FilePath .\inspection.txt
```
# turbofix
