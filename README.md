# LaMaInpaintProject — Quick install & build

## Requirements
- .NET SDK: install .NET SDK 10 (recommended) or any SDK that can build `net9.0-windows` projects.
- Visual Studio (optional): 2022/2023 with .NET desktop development workload if you prefer the IDE.
- Paint.NET: installed on the target machine (`C:\Program Files\paint.net\`).
- GPU drivers (optional): DirectML only if you want GPU acceleration.
- The repo already references `Microsoft.ML.OnnxRuntime.DirectML` NuGet package.

## Build (CLI)
1. Open a Developer PowerShell at the repository root.
2. Build the project (project-level build is required when specifying an RID):
   - Debug x64:
     `dotnet build .\LaMaInpaintProject.csproj -c Debug -r win-x64`
   - Release x64:
     `dotnet build .\LaMaInpaintProject.csproj -c Release -r win-x64`

If you prefer Visual Studio:
- Set Solution Platform to `x64` and Configuration to `Debug` or `Release`.
- Build the `LaMaInpaintProject` project (not the whole solution if you use an RID).

## Find the build outputs
After a successful build the main outputs will appear under the project `bin` folder. Examples:
- Debug RID output:
  `bin\Debug\net9.0-windows\win-x64\LaMaInpaintProject.dll`
- Release RID output:
  `bin\Release\net9.0-windows\win-x64\LaMaInpaintProject.dll`

The project is configured to copy the model file to the output as `model.onnx`. Look for:
- `bin\<Config>\net9.0-windows\win-x64\model.onnx`

Also check the output for any ONNX runtime files:
- `Microsoft.ML.OnnxRuntime*.dll`
- `DirectML.dll` (if present)

## Install the plugin into Paint.NET (manual)
1. Close Paint.NET.
2. Open an elevated PowerShell (Run as Administrator).
3. Copy files to the Paint.NET Effects folder:
4. Start Paint.NET and look for the effect (menu path: `Effects` → `AI Tools` → `AI Object Removal`).

## Install using the included script
A helper script is provided: `scripts/Install-PaintDotNet.ps1`
- It elevates itself and attempts to locate the build output, then copies the DLL, `model.onnx`, and ONNX runtime DLLs into `C:\Program Files\paint.net\Effects`.
- Run from repo root (will prompt for elevation):
- Debug:
 `.\scripts\Install-PaintDotNet.ps1 -Configuration Debug -Platform x64`
- If your build output ended up in a `win-x64` subfolder, the script will detect and copy from there.

## Troubleshooting
- Effect missing in Paint.NET:
- Confirm the DLL and `model.onnx` are present in `C:\Program Files\paint.net\Effects\`.
- Confirm you built x64 and copied x64 native DLLs.
- If the plugin fails to load, check Windows Event Viewer → Application for errors from `PaintDotNet.exe`.
- To avoid Paint.NET skipping the plugin due to initialization errors, ensure heavy initialization (ONNX session creation) is deferred until runtime or wrapped in try/catch.
- Permission issues copying to `C:\Program Files`: ensure PowerShell is elevated (Administrator).

## Notes
- The repository contains a simplified wrapper `LaMaEffect` for running inference outside Paint.NET. To integrate as a proper Paint.NET effect, implement the effect class that inherits the Paint.NET `PropertyBasedEffect`/`Effect` API and ensure initialization is deferred.
