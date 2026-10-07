# Builds VillagerSpaceProgram.dll and (optionally) installs the mod into KSP.
#   .\build.ps1                 build only
#   .\build.ps1 -Install        build, then copy GameData\VillagerSpaceProgram into KSP
# Uses the C# compiler that ships with Windows (.NET Framework 4, C# 5) and compiles
# against KSP's own DLLs, so no SDK or downloads are needed. KSP's DLLs are never copied.
param(
    [string]$Ksp = "C:\Users\minal\Games\Kerbal Space Program",
    [switch]$Install
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$managed = Join-Path $Ksp "KSP_x64_Data\Managed"
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$outDir = Join-Path $here "GameData\VillagerSpaceProgram\Plugins"
New-Item -ItemType Directory -Force $outDir | Out-Null

$refs = @("mscorlib", "System", "System.Core", "Assembly-CSharp", "Assembly-CSharp-firstpass",
          "UnityEngine", "UnityEngine.CoreModule", "UnityEngine.ImageConversionModule",
          "UnityEngine.ScreenCaptureModule", "UnityEngine.PhysicsModule", "UnityEngine.AnimationModule",
          "UnityEngine.IMGUIModule", "UnityEngine.UI") | ForEach-Object { "/r:" + (Join-Path $managed "$_.dll") }
$src = Get-ChildItem (Join-Path $here "src") -Filter *.cs | ForEach-Object { $_.FullName }

& $csc /nologo /noconfig /nostdlib+ /target:library /optimize+ /warn:4 "/out:$(Join-Path $outDir 'VillagerSpaceProgram.dll')" $refs $src
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Write-Host "built $(Join-Path $outDir 'VillagerSpaceProgram.dll')"

if ($Install) {
    $dst = Join-Path $Ksp "GameData\VillagerSpaceProgram"
    New-Item -ItemType Directory -Force $dst | Out-Null
    # Keep the dev-tour switch and its output if they exist in the install.
    robocopy (Join-Path $here "GameData\VillagerSpaceProgram") $dst /E /XF devtour.txt /XD tour /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "install copy failed" }
    Write-Host "installed to $dst"
}
exit 0
