param([switch]$Hardware)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $checks = @('DesignCheck')
    if ($Hardware) { $checks += @('InteractionCheck', 'FeaturesCheck', 'LifecycleCheck') }
    foreach ($check in $checks) {
        & $compiler /nologo /target:exe /platform:anycpu /codepage:65001 "/main:$check" "/out:tests\$check.exe" /resource:assets\Brightness.ico,Brightness.ico /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Xml.dll /reference:Accessibility.dll Brightness.cs Features.cs Design.cs "tests\$check.cs"
        if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $check" }
        & ".\tests\$check.exe"
        if ($LASTEXITCODE -ne 0) { throw "Check failed: $check" }
    }
} finally { Pop-Location }
