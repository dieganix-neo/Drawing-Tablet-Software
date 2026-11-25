$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $framework)) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $framework)) { throw 'The Windows .NET Framework C# compiler is required.' }
$buildDirectory = Join-Path $PSScriptRoot 'build'
$null = New-Item -ItemType Directory -Force -Path $buildDirectory
$exe = Join-Path $buildDirectory 'TabletDesk.exe'
$sourceFiles = @('Program.cs', 'Native.cs', 'Tablet.cs', 'PointerEngine.cs', 'TabletView.cs', 'ThemedFields.cs', 'Dashboard.cs')
$sources = $sourceFiles | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $framework /nologo /unsafe /target:winexe /optimize+ /warnaserror "/out:$exe" @sources
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }
Write-Output $exe

