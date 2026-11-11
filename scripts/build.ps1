param(
    [string] $Version,
    [string] $OutputDirectory,
    [switch] $RunTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Version) { $Version = (Get-Content -Raw (Join-Path $root 'VERSION')).Trim() }
if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "Invalid version: $Version"
}
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$work = Join-Path $OutputDirectory 'build'
New-Item -ItemType Directory -Force $work | Out-Null
$versionFile = Join-Path $work 'AssemblyVersion.cs'
@"
using System.Reflection;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName=".NET Framework 4.8")]
[assembly: AssemblyTitle("Trans Tool")]
[assembly: AssemblyProduct("Trans Tool")]
[assembly: AssemblyCompany("tangyuanx")]
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
"@ | Set-Content -Encoding UTF8 $versionFile

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (!(Test-Path $csc)) { throw '.NET Framework C# compiler not found.' }
$manifest = Join-Path $root 'src/app.manifest'
$sources = @(Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName) + $versionFile
$options = @('/nologo', '/codepage:65001', '/optimize+', '/warnaserror+',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll')
$app = Join-Path $work 'TransTool.exe'
& $csc @options '/target:winexe' '/main:TransTool.Program' "/win32manifest:$manifest" "/out:$app" @sources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }

if ($RunTests) {
    $testExe = Join-Path $work 'TransTool.Tests.exe'
    $testSources = @(Get-ChildItem (Join-Path $root 'tests') -Filter '*.cs' | ForEach-Object FullName)
    & $csc @options '/target:exe' '/main:TransTool.Tests' "/out:$testExe" @sources @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($iscc) { $compiler = $iscc.Source }
else { $compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
if (!(Test-Path $compiler)) { throw 'Inno Setup 6 compiler not found.' }
& $compiler "/DAppVersion=$Version" "/DApplicationPath=$app" "/DOutputPath=$OutputDirectory" (Join-Path $root 'packaging/TransTool.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
