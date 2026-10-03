using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Hosting;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alethic.Node.AspNet;

/// <summary>
/// The application's pool of Node engines, for an ASP.NET application, which has no container to keep one in.
/// </summary>
/// <remarks>
/// Made the first time it is wanted, from the application's <c>appSettings</c> and then whatever
/// <see cref="Configure"/> was given, and disposed of when the application shuts down. Its engines are prepared for
/// <see cref="NodeRequest"/> before anything else is done with them.
///
/// <list type="table">
/// <listheader><term>App setting</term><description>Option</description></listheader>
/// <item><term><c>Alethic:Node:EngineCount</c></term><description><see cref="NodeEnginePoolOptions.EngineCount"/></description></item>
/// <item><term><c>Alethic:Node:MaxConcurrencyPerEngine</c></term><description><see cref="NodeEnginePoolOptions.MaxConcurrencyPerEngine"/></description></item>
/// <item><term><c>Alethic:Node:AcquireTimeout</c></term><description><see cref="NodeEnginePoolOptions.AcquireTimeout"/>, as a <see cref="TimeSpan"/></description></item>
/// <item><term><c>Alethic:Node:LibNodePath</c></term><description><see cref="NodeEnginePoolOptions.LibNodePath"/>; <c>~/</c> is the application's root</description></item>
/// <item><term><c>Alethic:Node:BaseDirectory</c></term><description><see cref="NodeEnginePoolOptions.BaseDirectory"/>; <c>~/</c> is the application's root</description></item>
/// </list>
///
/// Node starts once per process. ASP.NET restarts an application in a new AppDomain of the same process, and the new
/// one cannot start Node again: its pool fails to start an engine until the process is recycled. Keep ASP.NET from
/// restarting applications in place where they run Node.
/// </remarks>
public static class AspNetNode
{

    /// <summary>
    /// The prefix of the app settings read.
    /// </summary>
    const string SettingsPrefix = "Alethic:Node:";

    /// <summary>
    /// How long disposing of the pool may hold up the application's shutdown.
    /// </summary>
    static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    static readonly object sync = new();

    static Action<NodeEnginePoolOptions>? configure;
    static ILoggerFactory loggerFactory = NullLoggerFactory.Instance;
    static IServiceProvider services = NoServices.Instance;
    static NodeEnginePool? pool;

    /// <summary>
    /// Configures the pool, beyond what the app settings do; before it is first wanted, such as in
    /// <c>Application_Start</c>.
    /// </summary>
    /// <param name="configure">Configures its options, after the app settings have.</param>
    /// <param name="loggerFactory">Logs for it; nothing where <see langword="null"/>.</param>
    /// <param name="services">What <see cref="NodeEnginePoolOptions.ConfigureEngine"/> is given; nothing where
    /// <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">The pool has been made already.</exception>
    public static void Configure(Action<NodeEnginePoolOptions> configure, ILoggerFactory? loggerFactory = null, IServiceProvider? services = null)
    {
        if (configure is null)
            throw new ArgumentNullException(nameof(configure));

        lock (sync)
        {
            if (pool is not null)
                throw new InvalidOperationException("The application's Node pool has been made already: configure it before it is first wanted.");

            AspNetNode.configure += configure;
            AspNetNode.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            AspNetNode.services = services ?? NoServices.Instance;
        }
    }

    /// <summary>
    /// The application's pool, made the first time it is wanted.
    /// </summary>
    public static NodeEnginePool Pool
    {
        get
        {
            lock (sync)
                return pool ??= Create();
        }
    }

    /// <summary>
    /// Makes the pool, and has it disposed of when the application shuts down.
    /// </summary>
    static NodeEnginePool Create()
    {
        var options = new NodeEnginePoolOptions();
        Read(options);
        configure?.Invoke(options);

        var own = options.ConfigureEngine;
        options.ConfigureEngine = async (provider, lease) =>
        {
            await NodeRequest.InstallAsync(lease);
            if (own is not null)
                await own(provider, lease);
        };

        var created = new NodeEnginePool(options, loggerFactory, services);
        if (HostingEnvironment.IsHosted)
            HostingEnvironment.RegisterObject(new Stopper(created));

        return created;
    }

    /// <summary>
    /// Sets options from the app settings that are set.
    /// </summary>
    /// <param name="options">The options.</param>
    static void Read(NodeEnginePoolOptions options)
    {
        if (Setting("EngineCount") is string engineCount)
            options.EngineCount = int.Parse(engineCount);
        if (Setting("MaxConcurrencyPerEngine") is string concurrency)
            options.MaxConcurrencyPerEngine = int.Parse(concurrency);
        if (Setting("AcquireTimeout") is string timeout)
            options.AcquireTimeout = TimeSpan.Parse(timeout);
        if (Setting("LibNodePath") is string libNodePath)
            options.LibNodePath = MapPath(libNodePath);
        if (Setting("BaseDirectory") is string baseDirectory)
            options.BaseDirectory = MapPath(baseDirectory);
    }

    /// <summary>
    /// An app setting, where it is set.
    /// </summary>
    /// <param name="name">Its name, after the prefix.</param>
    static string? Setting(string name)
    {
        var value = ConfigurationManager.AppSettings[SettingsPrefix + name];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// A path, with <c>~/</c> as the application's root.
    /// </summary>
    /// <param name="path">The path.</param>
    static string MapPath(string path)
    {
        return path.StartsWith("~/", StringComparison.Ordinal) && HostingEnvironment.IsHosted ? HostingEnvironment.MapPath(path) : path;
    }

    /// <summary>
    /// Disposes of the pool when the application shuts down, so no engine's thread calls into the AppDomain as it
    /// unloads.
    /// </summary>
    /// <param name="stopping">The pool.</param>
    sealed class Stopper(NodeEnginePool stopping) : IRegisteredObject
    {

        /// <summary>
        /// Disposes of the pool, for no longer than <see cref="StopTimeout"/>, which keeps IIS from ending the whole
        /// process over a shutdown that will not finish.
        /// </summary>
        /// <param name="immediate">Whether this is the final call.</param>
        public void Stop(bool immediate)
        {
            try
            {
                if (Task.Run(() => stopping.DisposeAsync().AsTask()).Wait(StopTimeout) == false)
                    Trace.TraceWarning("Alethic.Node.AspNet: the Node pool did not stop within {0}; shutting down without it.", StopTimeout);
            }
            catch (Exception e)
            {
                Trace.TraceWarning("Alethic.Node.AspNet: the Node pool did not stop cleanly: {0}", e);
            }
            finally
            {
                HostingEnvironment.UnregisterObject(this);
            }
        }

    }

    /// <summary>
    /// A provider of no services, for a pool given none.
    /// </summary>
    sealed class NoServices : IServiceProvider
    {

        /// <summary>
        /// The one instance.
        /// </summary>
        public static readonly NoServices Instance = new();

        /// <summary>
        /// Provides nothing.
        /// </summary>
        /// <param name="serviceType">The service asked for.</param>
        public object? GetService(Type serviceType) => null;

    }

}
