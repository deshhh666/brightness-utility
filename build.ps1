param([string]$Output = 'Brightness.exe')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
Push-Location $PSScriptRoot
try {
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$Output" /win32manifest:app.manifest /win32icon:assets\Brightness.ico /resource:assets\Brightness.ico,Brightness.ico /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Xml.dll /reference:Accessibility.dll Brightness.cs Features.cs Design.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
    Write-Output "Built: $PSScriptRoot\$Output"
} finally { Pop-Location }
