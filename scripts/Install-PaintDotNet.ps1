param(
    [string]$Configuration = 'Debug',
    [string]$Platform = 'x64',
    [string]$TargetFolder = 'C:\Program Files\paint.net\Effects',
    [string]$ProjectName = 'LaMaInpaintProject.dll',
    [string]$ModelRelativePath = 'model.onnx'
)

# Relaunch elevated if necessary
function Ensure-Elevated {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($current)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host "Elevation required. Relaunching as Administrator..."
        $args = @()
        if ($PSBoundParameters.Count -gt 0) {
            foreach ($k in $PSBoundParameters.Keys) {
                $v = $PSBoundParameters[$k]
                $args += "-$k `"$v`""
            }
        }
        $psiArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" $($args -join ' ')"
        Start-Process -FilePath pwsh -ArgumentList $psiArgs -Verb RunAs -WindowStyle Normal
        Exit
    }
}

Ensure-Elevated

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Common candidate output folders
$candidate1 = Join-Path -Path $repoRoot -ChildPath "bin\$Platform\$Configuration\net9.0-windows"
$candidate2 = Join-Path -Path $candidate1 -ChildPath "win-x64"

# Try to find the built DLL anywhere under bin if the above didn't match
$sourceDll = Get-ChildItem -Path (Join-Path $repoRoot 'bin') -Filter $ProjectName -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $sourceDll) {
    # Try the expected candidate locations
    if (Test-Path $candidate2) {
        $outputFolder = $candidate2
    } elseif (Test-Path $candidate1) {
        $outputFolder = $candidate1
    } else {
        Write-Error "Build output folder not found. Please run 'dotnet build -c $Configuration -r win-x64' first."
        Exit 1
    }
    $sourceDll = Get-ChildItem -Path $outputFolder -Filter $ProjectName -File -ErrorAction SilentlyContinue | Select-Object -First 1
}
else {
    $outputFolder = Split-Path -Parent $sourceDll.FullName
}

if (-not $sourceDll) {
    Write-Error "Could not locate '$ProjectName' in the bin output. Make sure the project is built and the configuration/platform match the parameters."
    Exit 1
}

Write-Host "Found build output folder: $outputFolder"
Write-Host "Preparing to copy files to: $TargetFolder"

# Ensure target exists
if (-not (Test-Path $TargetFolder)) {
    try {
        New-Item -ItemType Directory -Path $TargetFolder -Force | Out-Null
    } catch {
        Write-Error "Failed to create target folder '$TargetFolder': $_"
        Exit 1
    }
}

# Files to copy
$filesToCopy = @()
$filesToCopy += $sourceDll.FullName

# model: prefer output model first, fallback to project Models folder
$modelInOutput = Join-Path -Path $outputFolder -ChildPath $ModelRelativePath
if (Test-Path $modelInOutput) {
    $filesToCopy += $modelInOutput
} else {
    $modelInRepo = Join-Path -Path $repoRoot -ChildPath "Models\$ModelRelativePath"
    if (Test-Path $modelInRepo) {
        $filesToCopy += $modelInRepo
    } else {
        Write-Warning "Model file not found in output or Models\ folder. Looking for any model.onnx in repo..."
        $foundModel = Get-ChildItem -Path $repoRoot -Filter $ModelRelativePath -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($foundModel) { $filesToCopy += $foundModel.FullName }
    }
}

# ONNX runtime and DirectML related DLLs (copy any that exist next to the build output)
$runtimeDlls = Get-ChildItem -Path $outputFolder -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'Microsoft.ML.OnnxRuntime*' -or $_.Name -ieq 'DirectML.dll' }
foreach ($d in $runtimeDlls) { $filesToCopy += $d.FullName }

if ($filesToCopy.Count -eq 0) {
    Write-Error "No files discovered to copy. Aborting."
    Exit 1
}

# Copy files
$failed = @()
foreach ($f in $filesToCopy | Select-Object -Unique) {
    try {
        Write-Host "Copying: $f -> $TargetFolder"
        Copy-Item -Path $f -Destination $TargetFolder -Force -ErrorAction Stop
    } catch {
        Write-Warning "Failed to copy $f : $_"
        $failed += $f
    }
}

if ($failed.Count -gt 0) {
    Write-Error "Some files failed to copy. See warnings above."
    Exit 1
}

Write-Host "All files copied successfully. Restart Paint.NET to see the new effect."
Exit 0
