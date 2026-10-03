using System;
using System.IO;
using System.Runtime.InteropServices;

using Microsoft.JavaScript.NodeApi.Runtime;

namespace Alethic.Node;

/// <summary>
/// Gets the one embedding platform a process is allowed, creating it the first time.
/// </summary>
/// <remarks>
/// Node permits a single embedding platform per process. node-api-dotnet keeps it, as
/// <see cref="NodeEmbeddingPlatform.Current"/>, and refuses to construct a second; it neither hands out the one there
/// is nor creates it safely from two threads at once, and it does not say what library it was created from. This does
/// those: two pools in one application share the platform, as does any other code in the process that created one,
/// and a pool asking for a different library than the one it was created from is told so.
///
/// Node's statics are per AppDomain, as this is, and a process can hold more than one: ASP.NET on .NET Framework
/// starts a new AppDomain in the same process when an application restarts, while the native library the old one
/// loaded stays loaded. So a library already loaded when there is no platform here is one a platform elsewhere in the
/// process owns, and starting another would fail natively; that is refused instead, where it can be detected, which
/// is Windows.
/// </remarks>
static class NodeRuntimeHost
{

    static readonly object sync = new();

    /// <summary>
    /// The library the platform was created from here; <see langword="null"/> where node-api-dotnet found it, or the
    /// platform was created elsewhere.
    /// </summary>
    static string? loadedFrom;

    /// <summary>
    /// Whether the platform was created here, so that <see cref="loadedFrom"/> says where from.
    /// </summary>
    static bool created;

    /// <summary>
    /// Returns the process-wide platform, creating it from the given library on first use.
    /// </summary>
    /// <param name="libNodePath">The library, or <see langword="null"/> for node-api-dotnet to find.</param>
    /// <exception cref="InvalidOperationException"></exception>
    public static NodeEmbeddingPlatform GetOrCreate(string? libNodePath)
    {
        lock (sync)
        {
            if (NodeEmbeddingPlatform.Current is NodeEmbeddingPlatform current)
                return Verify(current, libNodePath);

            if (IsLoaded(libNodePath))
                throw new InvalidOperationException(
                    $"The Node runtime '{libNodePath ?? LibNodeLocator.FileName}' is already loaded in this process, by something other than this AppDomain, such as an earlier one. Node cannot be started a second time in one process.");

            var platform = new NodeEmbeddingPlatform(new NodeEmbeddingPlatformSettings() { LibNodePath = libNodePath });
            loadedFrom = libNodePath;
            created = true;
            return platform;
        }
    }

    /// <summary>
    /// Whether the library is already loaded in this process, where that can be told.
    /// </summary>
    /// <param name="libNodePath">The library, or <see langword="null"/> for the platform's file name.</param>
    static bool IsLoaded(string? libNodePath)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && GetModuleHandle(Path.GetFileName(libNodePath ?? LibNodeLocator.FileName)) != IntPtr.Zero;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string moduleName);

    /// <summary>
    /// Confirms a caller is asking for the library the platform there is was created from, where that is known: a
    /// platform created elsewhere, or from wherever node-api-dotnet found the library, is taken as it is.
    /// </summary>
    /// <param name="platform">The platform there is.</param>
    /// <param name="libNodePath">The library asked for, or <see langword="null"/> for whichever node-api-dotnet found.</param>
    /// <exception cref="InvalidOperationException"></exception>
    static NodeEmbeddingPlatform Verify(NodeEmbeddingPlatform platform, string? libNodePath)
    {
        if (created && libNodePath is not null && loadedFrom is not null && string.Equals(loadedFrom, libNodePath, StringComparison.OrdinalIgnoreCase) == false)
            throw new InvalidOperationException(
                $"A Node runtime is already loaded from '{loadedFrom}'. Only one may be loaded per process, so '{libNodePath}' cannot also be used.");

        return platform;
    }

}
