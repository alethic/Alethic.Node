using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Alethic.Node;

/// <summary>
/// Says where the native Node library is, where node-api-dotnet would not find it by itself.
/// </summary>
/// <remarks>
/// Given no path, node-api-dotnet looks under the application's base directory, at
/// <c>runtimes/&lt;rid&gt;/native</c>, where the Microsoft.JavaScript.LibNode packages put the library, and then
/// loads it by name, which finds it beside a published application. The one host that leaves it elsewhere is
/// ASP.NET on .NET Framework, whose base directory is the site while its binaries are in the AppDomain's private
/// <c>bin</c>: there the library is looked for under that.
/// </remarks>
static class LibNodeLocator
{

    /// <summary>
    /// The library to load: the configured one; on .NET Framework, the one under the AppDomain's private <c>bin</c>,
    /// where it has one; or <see langword="null"/>, for node-api-dotnet to find.
    /// </summary>
    /// <param name="configured"></param>
    /// <exception cref="FileNotFoundException"></exception>
    public static string? Locate(string? configured)
    {
        if (string.IsNullOrEmpty(configured) == false)
            return File.Exists(configured)
                ? configured
                : throw new FileNotFoundException($"Configured Node runtime not found at '{configured}'.", configured);

#if NETFRAMEWORK
        var bin = AppDomain.CurrentDomain.RelativeSearchPath;
        if (string.IsNullOrEmpty(bin) == false)
        {
            var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, bin, "runtimes", "win-" + Architecture, "native", FileName);
            if (File.Exists(file))
                return file;
        }
#endif

        return null;
    }

    /// <summary>
    /// The library's file name on this platform.
    /// </summary>
    public static string FileName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "libnode.dll"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libnode.dylib"
        : "libnode.so";

#if NETFRAMEWORK

    /// <summary>
    /// The process's architecture, as a runtime identifier names it. .NET Framework runs on Windows alone.
    /// </summary>
    static string Architecture => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.Arm => "arm",
        var other => other.ToString().ToLowerInvariant(),
    };

#endif

}
