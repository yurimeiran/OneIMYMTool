#Requires -Version 5.1

$ErrorActionPreference = 'Stop'

$script:OimDir  = 'C:\Program Files\One Identity\One Identity Manager'
$script:DllPath = Join-Path $PSScriptRoot 'OneIMModule.dll'
$script:SrcPath = Join-Path $PSScriptRoot 'OneIMModule.cs'

# Compile a pure-C# resolver — a PS script block delegate cannot be used here because
# invoking it requires the PS runtime, which loads assemblies, which re-fires the event.
Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace OneIMModule
{
    public static class AssemblyResolver
    {
        private static string _dir;
        private static readonly HashSet<string> _loading =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string searchDir)
        {
            _dir = searchDir;
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
        }

        public static void Unregister()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= OnResolve;
        }

        private static Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                    return a;

            if (!_loading.Add(name)) return null;
            try
            {
                string path = Path.Combine(_dir, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFile(path) : null;
            }
            finally { _loading.Remove(name); }
        }
    }
}
'@

[OneIMModule.AssemblyResolver]::Register($script:OimDir)

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
    try { [OneIMModule.AssemblyResolver]::Unregister() } catch { }
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
