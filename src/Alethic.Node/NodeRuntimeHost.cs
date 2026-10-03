using System;
using System.IO;
using System.Runtime.InteropServices;

using Microsoft.JavaScript.NodeApi.Runtime;

namespace Alethic.Node;

/// <summary>
/// Owns the one embedding platform a process is allowed.
/// </summary>
/// <remarks>
/// Node permits a single embedding platform per process, so this is a static gate rather than a
/// registered service: two pools in one application must share it, and a second platform would fail
/// at native level rather than raise anything a caller could handle.
///
/// The gate is per copy of this assembly, and a process can hold more than one: ASP.NET on .NET
/// Framework starts a new AppDomain in the same process when an application restarts, with its own
/// statics, while the native library the old one loaded stays loaded. So a library already loaded
/// when there is no platform here is one a platform elsewhere in the process owns, and starting
/// another would fail natively; that is refused instead, where it can be detected, which is Windows.
/// </remarks>
static class NodeRuntimeHost
{

    static readonly object sync = new();

    static NodeEmbeddingPlatform? platform;
    static string? loadedFrom;

    /// <summary>
    /// Returns the process-wide platform, creating it from the given library on first use.
    /// </summary>
    /// <param name="libNodePath"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public static NodeEmbeddingPlatform GetOrCreate(string libNodePath)
    {
        if (platform is not null)
            return Verify(libNodePath);

        lock (sync)
        {
            if (platform is not null)
                return Verify(libNodePath);

            if (IsLoaded(libNodePath))
                throw new InvalidOperationException(
                    $"The Node runtime '{libNodePath}' is already loaded in this process, by something other than this copy of {typeof(NodeRuntimeHost).Assembly.GetName().Name}, such as an earlier AppDomain. Node cannot be started a second time in one process.");

            platform = new NodeEmbeddingPlatform(new NodeEmbeddingPlatformSettings() { LibNodePath = libNodePath });
            loadedFrom = libNodePath;
            return platform;
        }
    }

    /// <summary>
    /// Whether the library is already loaded in this process, where that can be told.
    /// </summary>
    /// <param name="libNodePath"></param>
    static bool IsLoaded(string libNodePath)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && GetModuleHandle(Path.GetFileName(libNodePath)) != IntPtr.Zero;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string moduleName);

    /// <summary>
    /// Confirms a second caller is asking for the library already loaded.
    /// </summary>
    /// <param name="libNodePath"></param>
    /// <exception cref="InvalidOperationException"></exception>
    static NodeEmbeddingPlatform Verify(string libNodePath)
    {
        if (string.Equals(loadedFrom, libNodePath, StringComparison.OrdinalIgnoreCase) == false)
            throw new InvalidOperationException(
                $"A Node runtime is already loaded from '{loadedFrom}'. Only one may be loaded per process, so '{libNodePath}' cannot also be used.");

        return platform!;
    }

}
