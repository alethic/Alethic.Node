using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Alethic.Node;

/// <summary>
/// Finds the native Node library to embed.
/// </summary>
static class LibNodeLocator
{

    /// <summary>
    /// Resolves the library, preferring an explicitly configured path.
    /// </summary>
    /// <remarks>
    /// Two layouts have to be handled because publishing flattens the native asset next to the
    /// binary while an ordinary build leaves it under a runtime identifier.
    /// </remarks>
    /// <param name="configured"></param>
    /// <exception cref="FileNotFoundException"></exception>
    public static string Locate(string? configured)
    {
        if (string.IsNullOrEmpty(configured) == false)
            return File.Exists(configured)
                ? configured!
                : throw new FileNotFoundException($"Configured Node runtime not found at '{configured}'.", configured);

        var file = FileName;

        var flat = Path.Combine(AppContext.BaseDirectory, file);
        if (File.Exists(flat))
            return flat;

        var rid = Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeIdentifier, "native", file);
        if (File.Exists(rid))
            return rid;

        throw new FileNotFoundException(
            $"No {file} beside the application or under runtimes/{RuntimeIdentifier}/native. " +
            $"Reference the Microsoft.JavaScript.LibNode package for this runtime identifier, or set the runtime path explicitly.");
    }

    /// <summary>
    /// The runtime identifier the native asset is laid out under.
    /// </summary>
    /// <remarks>
    /// .NET Framework has no <c>RuntimeInformation.RuntimeIdentifier</c>, and runs on Windows alone, so there it is
    /// Windows and the process's architecture.
    /// </remarks>
    static string RuntimeIdentifier
    {
        get
        {
#if NETFRAMEWORK
            return "win-" + RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                var other => other.ToString().ToLowerInvariant(),
            };
#else
            return RuntimeInformation.RuntimeIdentifier;
#endif
        }
    }

    /// <summary>
    /// Platform-specific library file name.
    /// </summary>
    static string FileName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "libnode.dll"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libnode.dylib"
        : "libnode.so";

}
