using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Alethic.Node;

/// <summary>
/// A JavaScript module, identified by where it lives on disk.
/// </summary>
/// <remarks>
/// A path, because Node's own loader resolves paths: a module is loaded with <c>require</c> and
/// cached in <c>require.cache</c> by resolved filename, which is what gives it one instance per
/// runtime with its module scope intact. Nothing here caches anything — Node already does, the same
/// way it does for any other program.
///
/// A module may be CommonJS or an ES module: Node's <c>require</c> loads an ES module whose graph has no
/// top-level await, and one that has is imported instead, where the work is asynchronous. A module on
/// disk can <c>import()</c> others, so a bundle need not be one file.
/// </remarks>
public abstract class NodeModuleSource
{

    /// <summary>
    /// A module on disk.
    /// </summary>
    /// <param name="path"></param>
    public static NodeModuleSource FromFile(string path) => new FileSource(path);

    /// <summary>
    /// Name reported in stack traces.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// The absolute path Node loads this module from.
    /// </summary>
    /// <param name="cancellationToken"></param>
    public abstract ValueTask<string> ResolveAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public override string ToString() => Name;

    sealed class FileSource : NodeModuleSource
    {

        readonly string path;

        public FileSource(string path)
        {
            this.path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
        }

        public override string Name => Path.GetFileName(path);

        public override ValueTask<string> ResolveAsync(CancellationToken cancellationToken)
        {
            if (File.Exists(path) == false)
                throw new FileNotFoundException($"No module at '{path}'.", path);

            return new(path);
        }

    }

}
