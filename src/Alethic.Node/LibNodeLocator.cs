using System;
using System.Diagnostics;
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
/// <c>bin</c>: there the library is looked for under that, at <c>runtimes/&lt;rid&gt;/native</c> or, where the build put
/// it beside the site's assemblies, in <c>bin</c> itself.
///
/// The Microsoft.JavaScript.LibNode packages are 64-bit alone: there is no x86 build of LibNode 26. A 32-bit process
/// is told so, rather than left to fail loading a library no package provides, unless it names a library itself.
///
/// And it is LibNode 26 or later that node-api-dotnet's embedding calls are written for. An older libnode, such as the
/// 20.1800 packages, does not fail those calls: it ends the process, with nothing raised and nothing logged. Where
/// the library says its version, as a Windows one does in its version resource, an older one is refused before it is
/// loaded.
/// </remarks>
static class LibNodeLocator
{

    /// <summary>
    /// The oldest Node major version whose libnode node-api-dotnet's embedding calls are written for.
    /// </summary>
    public const int MinimumMajorVersion = 26;

    /// <summary>
    /// The library to load: the configured one; on .NET Framework, the one under the AppDomain's private <c>bin</c>,
    /// where it has one; or <see langword="null"/>, for node-api-dotnet to find.
    /// </summary>
    /// <param name="configured"></param>
    /// <exception cref="FileNotFoundException"></exception>
    /// <exception cref="PlatformNotSupportedException"></exception>
    public static string? Locate(string? configured)
    {
        if (string.IsNullOrEmpty(configured) == false)
            return File.Exists(configured)
                ? configured
                : throw new FileNotFoundException($"Configured Node runtime not found at '{configured}'.", configured);

        if (Environment.Is64BitProcess == false)
            throw new PlatformNotSupportedException(
                "The Node runtime is 64-bit only: LibNode has no x86 build. Run the process as 64-bit (x64 or arm64; for IIS, an application pool with 32-bit applications disabled), or set LibNodePath to a 32-bit library of your own.");

#if NETFRAMEWORK
        var bin = AppDomain.CurrentDomain.RelativeSearchPath;
        if (string.IsNullOrEmpty(bin) == false)
        {
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, bin);
            foreach (var file in new[] { Path.Combine(root, "runtimes", "win-" + Architecture, "native", FileName), Path.Combine(root, FileName) })
                if (File.Exists(file))
                    return file;
        }
#endif

        return null;
    }

    /// <summary>
    /// Refuses a library older than <see cref="MinimumMajorVersion"/>, where its version can be read: on Windows, from
    /// the library to be loaded, or, where node-api-dotnet is to find it, from where it would be found under the
    /// application's base directory.
    /// </summary>
    /// <param name="path">The library to load, from <see cref="Locate"/>.</param>
    /// <exception cref="InvalidOperationException"></exception>
    public static void Verify(string? path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) == false)
            return;

        var file = path;
        if (file is null)
            foreach (var candidate in new[] { Path.Combine(AppContext.BaseDirectory, "runtimes", "win-" + Architecture, "native", FileName), Path.Combine(AppContext.BaseDirectory, FileName) })
                if (File.Exists(candidate))
                {
                    file = candidate;
                    break;
                }

        if (file is null || File.Exists(file) == false)
            return;

        var version = FileVersionInfo.GetVersionInfo(file);
        if (version.FileMajorPart > 0 && version.FileMajorPart < MinimumMajorVersion)
            throw new InvalidOperationException(
                $"The Node runtime at '{file}' is version {version.FileVersion}, and Alethic.Node needs LibNode {MinimumMajorVersion} or later: Microsoft.JavaScript.LibNode.<rid> 26.1.2 or later. An older libnode does not answer the embedding calls node-api-dotnet makes, and would end the process when started.");
    }

    /// <summary>
    /// The library's file name on this platform.
    /// </summary>
    public static string FileName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "libnode.dll"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libnode.dylib"
        : "libnode.so";

    /// <summary>
    /// The process's architecture, as a runtime identifier names it.
    /// </summary>
    static string Architecture => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        var other => other.ToString().ToLowerInvariant(),
    };

}
