using System;
using System.Configuration;
using System.Web.Hosting;

namespace Alethic.Node.AspNet;

/// <summary>
/// The <c>alethic.node</c> section of <c>web.config</c>: the application's default pool of Node engines, made where
/// <see cref="System.Web.HttpRuntime.WebObjectActivator"/> supplies none.
/// </summary>
/// <remarks>
/// <code language="xml"><![CDATA[
/// <configSections>
///   <section name="alethic.node" type="Alethic.Node.AspNet.NodeSection, Alethic.Node.AspNet" />
/// </configSections>
///
/// <alethic.node engineCount="2" maxConcurrencyPerEngine="4" acquireTimeout="00:00:10" />
/// ]]></code>
///
/// Read from the application's root: the pool is the application's, whatever folder a request is for. A site without
/// the section gets every default.
/// </remarks>
public sealed class NodeSection : ConfigurationSection
{

    /// <summary>
    /// The section's name in <c>web.config</c>.
    /// </summary>
    public const string SectionName = "alethic.node";

    /// <summary>
    /// The number of engines: <see cref="NodeEnginePoolOptions.EngineCount"/>. One where not set.
    /// </summary>
    [ConfigurationProperty("engineCount", DefaultValue = 1)]
    [IntegerValidator(MinValue = 1)]
    public int EngineCount
    {
        get => (int)this["engineCount"];
        set => this["engineCount"] = value;
    }

    /// <summary>
    /// The leases one engine takes at a time: <see cref="NodeEnginePoolOptions.MaxConcurrencyPerEngine"/>. Four where
    /// not set.
    /// </summary>
    [ConfigurationProperty("maxConcurrencyPerEngine", DefaultValue = 4)]
    [IntegerValidator(MinValue = 1)]
    public int MaxConcurrencyPerEngine
    {
        get => (int)this["maxConcurrencyPerEngine"];
        set => this["maxConcurrencyPerEngine"] = value;
    }

    /// <summary>
    /// How long an acquisition waits for capacity: <see cref="NodeEnginePoolOptions.AcquireTimeout"/>. Ten seconds
    /// where not set.
    /// </summary>
    [ConfigurationProperty("acquireTimeout", DefaultValue = "00:00:10")]
    public TimeSpan AcquireTimeout
    {
        get => (TimeSpan)this["acquireTimeout"];
        set => this["acquireTimeout"] = value;
    }

    /// <summary>
    /// The native Node library: <see cref="NodeEnginePoolOptions.LibNodePath"/>; <c>~/</c> is the application's root.
    /// Found where the Microsoft.JavaScript.LibNode packages put it where not set.
    /// </summary>
    [ConfigurationProperty("libNodePath", DefaultValue = "")]
    public string LibNodePath
    {
        get => (string)this["libNodePath"];
        set => this["libNodePath"] = value;
    }

    /// <summary>
    /// The root for Node's package resolution: <see cref="NodeEnginePoolOptions.BaseDirectory"/>; <c>~/</c> is the
    /// application's root. The application's base directory where not set.
    /// </summary>
    [ConfigurationProperty("baseDirectory", DefaultValue = "")]
    public string BaseDirectory
    {
        get => (string)this["baseDirectory"];
        set => this["baseDirectory"] = value;
    }

    /// <summary>
    /// The pool's options, as the section sets them.
    /// </summary>
    internal NodeEnginePoolOptions ToOptions()
    {
        return new NodeEnginePoolOptions()
        {
            EngineCount = EngineCount,
            MaxConcurrencyPerEngine = MaxConcurrencyPerEngine,
            AcquireTimeout = AcquireTimeout,
            LibNodePath = MapPath(LibNodePath),
            BaseDirectory = MapPath(BaseDirectory),
        };
    }

    /// <summary>
    /// A path, with <c>~/</c> as the application's root; <see langword="null"/> where empty.
    /// </summary>
    /// <param name="path">The path.</param>
    static string? MapPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.StartsWith("~/", StringComparison.Ordinal) && HostingEnvironment.IsHosted ? HostingEnvironment.MapPath(path) : path;
    }

}
