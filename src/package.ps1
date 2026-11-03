<#
Builds the Windows release: a Native AOT publish, packed by Velopack into
  CheeseGit-<rid>-Setup.exe         per-user installer (no admin), shortcuts, uninstall entry, in-app updates
  CheeseGit-<rid>-Portable.zip      unzip and run; updates still work through the bundled Update.exe
  CheeseGit-<v>-<rid>-full.nupkg    what the updater downloads
Prints the sizes tracked in docs/performance.md. The Windows counterpart of package.sh.

  .\package.ps1 -Version <x.y.z> [-Rid win-x64|win-arm64] [-Out <dir>] [-Notes <release-notes.md>] [-Previous <dir>]

-Previous holds the last release of this channel (vpk download github): vpk then builds a delta package
against it, so updates download only what changed.

The version is required and must be above 0.6.2: the old Tauri app's releases share the GitHub repository.
The Velopack channel is the RID, so every architecture can share one GitHub release. Signing goes through
vpk's own environment variables (VPK_SIGN_TEMPLATE, or the Azure Trusted Signing ones), so no secret passes
through this script.

Native AOT does not cross-compile between operating systems, so this runs on Windows. It needs the
.NET 10 SDK and, for the AOT linker, Visual Studio (or Build Tools) with the "Desktop development
with C++" workload. Works in Windows PowerShell 5.1 and PowerShell 7.
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Rid = "win-x64",
    [string]$Out = (Join-Path $env:TEMP "cheesegit-package"),
    [string]$Notes = "",
    [string]$Previous = ""
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Invoke-Checked([scriptblock]$Command, [string]$What) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)" }
}

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
$publish = Join-Path $Out "publish"
$releases = Join-Path $Out "releases"
New-Item -ItemType Directory -Force $releases | Out-Null
if ($Previous) { Copy-Item -Recurse -Force (Join-Path $Previous "*") $releases }

Invoke-Checked { dotnet publish CheeseGit.App -c Release -r $Rid -o $publish -p:Version=$Version --nologo -v q } "dotnet publish"
Invoke-Checked { dotnet tool restore | Out-Null } "dotnet tool restore"

# The build generates the .ico from Assets/icon.png (CheeseGit.App.csproj, GenerateAppIcon).
$icon = Join-Path $PSScriptRoot "CheeseGit.App\obj\CheeseGit.ico"
$log = Join-Path $Out "vpk.log"
$notesArgs = @()
if ($Notes) { $notesArgs = @("--releaseNotes", (Resolve-Path $Notes).Path) }
# Windows PowerShell 5.1 turns redirected stderr from a native command into a terminating error under "Stop".
$ErrorActionPreference = "Continue"
dotnet vpk pack --packId CheeseGit --packVersion $Version --packTitle CheeseGit --packAuthors "Peter Lenkefi" `
    --packDir $publish --mainExe CheeseGit.exe --icon $icon --runtime $Rid --channel $Rid --outputDir $releases @notesArgs *> $log
$vpkExit = $LASTEXITCODE
$ErrorActionPreference = "Stop"
if ($vpkExit -ne 0) { Get-Content $log; throw "vpk pack failed (exit code $vpkExit)" }

# Invariant culture: a locale with a decimal comma would print "22,3 MB".
$inv = [Globalization.CultureInfo]::InvariantCulture
function Show-Size([string]$Name, [object[]]$Files) {
    $bytes = ($Files | Measure-Object -Property Length -Sum).Sum
    [Console]::WriteLine([string]::Format($inv, "{0,-28} {1,6:F1} MB", $Name, $bytes / 1e6))
}
Show-Size "publish folder" (Get-ChildItem $publish -Recurse -File)
Show-Size "  CheeseGit.exe (AOT binary)" (Get-Item (Join-Path $publish "CheeseGit.exe"))
Get-ChildItem $releases -File | Where-Object { $_.Name -match "Setup\.exe$|Portable\.zip$|-full\.nupkg$" } |
    ForEach-Object { Show-Size $_.Name @($_) }
Write-Output "output: $releases"
