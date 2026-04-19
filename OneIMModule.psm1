#Requires -Version 5.1
<#
.SYNOPSIS
    PowerShell module for One Identity Manager operations.

.DESCRIPTION
    Provides cmdlets for:
      Connect-OneIM        - Authenticate and open a session
      Disconnect-OneIM     - Close the session
      Get-OneIMSession     - Return the current active session object
      Invoke-OneIMCompile  - Compile the OIM database

    On first import, the module compiles OneIMModule.cs against the OIM DLLs
    and writes OneIMModule.dll alongside this file. Subsequent imports skip
    the compile step and load the cached DLL directly.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Paths ────────────────────────────────────────────────────────────────────

$script:OimDir    = 'C:\Program Files\One Identity\One Identity Manager'
$script:ModuleDir = $PSScriptRoot
$script:DllPath   = Join-Path $script:ModuleDir 'OneIMModule.dll'
$script:SrcPath   = Join-Path $script:ModuleDir 'OneIMModule.cs'

# ── Assembly resolver ─────────────────────────────────────────────────────────
# Ensures that any OIM DLL referenced transitively can be found at runtime.

$script:OimDirCapture = $script:OimDir
$script:Resolver = [System.ResolveEventHandler]{
    param($sender, $resolveArgs)
    $asmName = [System.Reflection.AssemblyName]::new($resolveArgs.Name).Name
    $candidate = Join-Path $script:OimDirCapture "$asmName.dll"
    if (Test-Path $candidate) {
        return [System.Reflection.Assembly]::LoadFile($candidate)
    }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($script:Resolver)

# ── Load required OIM assemblies ──────────────────────────────────────────────

$script:RequiredDlls = @('VI.Base', 'VI.DB', 'VI.DB.Compile', 'NLog')

foreach ($dll in $script:RequiredDlls) {
    $path = Join-Path $script:OimDir "$dll.dll"
    if (-not (Test-Path $path)) {
        throw "Required OIM assembly not found: $path"
    }
    [System.Reflection.Assembly]::LoadFile($path) | Out-Null
}

Write-Verbose "OIM assemblies loaded."

# ── Compile OneIMModule.dll if not yet built ──────────────────────────────────

if (-not (Test-Path $script:DllPath)) {

    Write-Host "OneIMModule.dll not found — compiling from source..." -ForegroundColor Cyan

    if (-not (Test-Path $script:SrcPath)) {
        throw "C# source not found: $script:SrcPath"
    }

    # Collect reference paths needed by the C# source.
    $refs = @(
        [System.Management.Automation.PSCmdlet].Assembly.Location
    )
    foreach ($dll in $script:RequiredDlls) {
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

# ── Import the compiled binary module ─────────────────────────────────────────

Import-Module $script:DllPath -Global -Force

Write-Verbose "OneIMModule ready. Cmdlets: Connect-OneIM, Disconnect-OneIM, Get-OneIMSession, Invoke-OneIMCompile"

# ── Cleanup on module removal ─────────────────────────────────────────────────

$MyInvocation.MyCommand.ScriptBlock.Module.OnRemove = {
    try {
        [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($script:Resolver)
    } catch {}
    try {
        if ([OneIMModule.OneIMSessionStore]::Current -ne $null) {
            [OneIMModule.OneIMSessionStore]::Current.Dispose()
        }
        $factory = [OneIMModule.OneIMSessionStore]::Factory
        if ($factory -ne $null) {
            ($factory -as [System.IDisposable])?.Dispose()
        }
    } catch {}
}
