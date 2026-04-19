#Requires -Version 5.1

$ErrorActionPreference = 'Stop'

$script:OimDir  = 'C:\Program Files\One Identity\One Identity Manager'
$script:DllPath = Join-Path $PSScriptRoot 'OneIMModule.dll'
$script:SrcPath = Join-Path $PSScriptRoot 'OneIMModule.cs'

# Register assembly resolver so OIM transitive dependencies are found at runtime
$script:OimDirLocal = $script:OimDir
$script:Resolver = [System.ResolveEventHandler] {
    param($sender, $resolveArgs)
    $asmName  = ([System.Reflection.AssemblyName] $resolveArgs.Name).Name
    $fullPath = Join-Path $script:OimDirLocal "$asmName.dll"
    if (Test-Path $fullPath) {
        return [System.Reflection.Assembly]::LoadFile($fullPath)
    }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($script:Resolver)

# Load required OIM assemblies
foreach ($dll in @('VI.Base', 'VI.DB', 'VI.DB.Compile', 'NLog')) {
    $path = Join-Path $script:OimDir "$dll.dll"
    if (-not (Test-Path $path)) {
        throw "Required OIM assembly not found: $path"
    }
    [System.Reflection.Assembly]::LoadFile($path) | Out-Null
}

Write-Verbose "OIM assemblies loaded."

# Compile the binary module if it does not exist yet
if (-not (Test-Path $script:DllPath)) {

    Write-Host "OneIMModule.dll not found - compiling from source..." -ForegroundColor Cyan

    if (-not (Test-Path $script:SrcPath)) {
        throw "C# source not found: $script:SrcPath"
    }

    $refs = @( [System.Management.Automation.PSCmdlet].Assembly.Location )
    foreach ($dll in @('VI.Base', 'VI.DB', 'VI.DB.Compile', 'NLog')) {
        $refs += Join-Path $script:OimDir "$dll.dll"
    }

    $source = Get-Content $script:SrcPath -Raw

    Add-Type `
        -TypeDefinition $source `
        -ReferencedAssemblies $refs `
        -OutputAssembly $script:DllPath `
        -OutputType Library `
        -Language CSharp

    Write-Host "Compiled successfully -> $script:DllPath" -ForegroundColor Green
}

# Import the compiled binary cmdlet DLL
Import-Module $script:DllPath -Global -Force

Write-Verbose "OneIMModule ready. Cmdlets: Connect-OneIM, Disconnect-OneIM, Get-OneIMSession, Invoke-OneIMCompile"

# Cleanup on module removal
$MyInvocation.MyCommand.ScriptBlock.Module.OnRemove = {
    try {
        [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($script:Resolver)
    } catch { }
    try {
        if ([OneIMModule.OneIMSessionStore]::Current -ne $null) {
            [OneIMModule.OneIMSessionStore]::Current.Dispose()
        }
    } catch { }
    try {
        $f = [OneIMModule.OneIMSessionStore]::Factory -as [System.IDisposable]
        if ($f -ne $null) { $f.Dispose() }
    } catch { }
}
