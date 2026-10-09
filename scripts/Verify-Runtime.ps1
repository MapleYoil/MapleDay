param([Parameter(Mandatory = $true)][string]$AppDirectory)
$ErrorActionPreference = 'Stop'
$appRoot = [IO.Path]::GetFullPath($AppDirectory)
$deps = Get-Content -LiteralPath (Join-Path $appRoot 'MapleDay.deps.json') -Raw | ConvertFrom-Json
$runtimeConfig = Get-Content -LiteralPath (Join-Path $appRoot 'MapleDay.runtimeconfig.json') -Raw | ConvertFrom-Json
$target = $deps.targets.PSObject.Properties[$deps.runtimeTarget.name].Value
$runtimePack = @($target.PSObject.Properties | Where-Object Name -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*')
if ($runtimePack.Count -ne 1) { throw 'Self-contained .NET runtime is missing from MapleDay.deps.json.' }
$assets = @($runtimePack[0].Value.runtime.PSObject.Properties.Name) + @($runtimePack[0].Value.native.PSObject.Properties.Name)
$webRuntime = @($target.PSObject.Properties | Where-Object Name -like 'runtimepack.Microsoft.AspNetCore.App.Runtime.win-x64/*')
if ($webRuntime.Count -ne 1) { throw 'Self-contained ASP.NET runtime is missing from MapleDay.deps.json.' }
$assets += @($webRuntime[0].Value.runtime.PSObject.Properties.Name)
if ($assets -notcontains 'Microsoft.AspNetCore.Server.Kestrel.Core.dll') { throw 'Kestrel runtime is missing.' }
foreach ($required in @('coreclr.dll', 'System.Private.CoreLib.dll')) {
    if ($assets -notcontains $required) { throw "Runtime dependency manifest is missing: $required" }
}
foreach ($asset in $assets) {
    if (-not [IO.File]::Exists((Join-Path $appRoot $asset))) { throw "Runtime dependency file is missing: $asset" }
}
if (-not $runtimeConfig.runtimeOptions.includedFrameworks -or $runtimeConfig.runtimeOptions.framework) {
    throw 'MapleDay runtime configuration must be self-contained.'
}

# Initialize the native .NET host only. This resolves the runtime and dependency
# graph, but never calls hostfxr_run_app, managed Main, or any WinUI window.
if (-not ('MapleDay.Build.RuntimeHost' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace MapleDay.Build {
    public static class RuntimeHost {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32")]
        private static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Initialize(int argc, IntPtr argv, IntPtr parameters, out IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Close(IntPtr context);
        public static int Verify(string hostPath, string application) {
            var module = LoadLibraryW(hostPath);
            if (module == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var argument = Marshal.StringToHGlobalUni(application);
            var argv = Marshal.AllocHGlobal(IntPtr.Size);
            IntPtr context = IntPtr.Zero;
            try {
                var initialize = (Initialize)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module, "hostfxr_initialize_for_dotnet_command_line"), typeof(Initialize));
                var close = (Close)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module, "hostfxr_close"), typeof(Close));
                Marshal.WriteIntPtr(argv, argument);
                try { return initialize(1, argv, IntPtr.Zero, out context); }
                finally { if (context != IntPtr.Zero) close(context); }
            } finally {
                Marshal.FreeHGlobal(argv);
                Marshal.FreeHGlobal(argument);
                FreeLibrary(module);
            }
        }
    }
}
'@
}
$result = [MapleDay.Build.RuntimeHost]::Verify((Join-Path $appRoot 'hostfxr.dll'), (Join-Path $appRoot 'MapleDay.dll'))
if ($result -lt 0) { throw ('Native .NET host initialization failed: 0x{0:X8}' -f $result) }
Write-Output "Self-contained .NET runtime manifest, $($assets.Count) assets, and native host initialization verified (app not started)."
