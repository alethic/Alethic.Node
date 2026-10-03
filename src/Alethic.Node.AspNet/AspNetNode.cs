using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web;
using System.Web.Configuration;
using System.Web.Hosting;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alethic.Node.AspNet;

/// <summary>
/// The application's pool of Node engines.
/// </summary>
/// <remarks>
/// The pool <see cref="HttpRuntime.WebObjectActivator"/> supplies, where the site has set one that supplies a
/// <see cref="NodeEnginePool"/>; otherwise a default, made the first time it is wanted from the <c>alethic.node</c>
/// section of <c>web.config</c> (<see cref="NodeSection"/>) and disposed of when the application shuts down. A pool the
/// activator supplies is the site's to dispose of.
///
/// Node starts once per process. ASP.NET restarts an application in a new AppDomain of the same process, and the new
/// one cannot start Node again: its pool fails to start an engine until the process is recycled. Keep ASP.NET from
/// restarting applications in place where they run Node.
/// </remarks>
public static class AspNetNode
{

    /// <summary>
    /// How long disposing of the default pool may hold up the application's shutdown.
    /// </summary>
    static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    static readonly object sync = new();

    static NodeEnginePool? fallback;

    /// <summary>
    /// The application's pool: the one <see cref="HttpRuntime.WebObjectActivator"/> supplies, or the default.
    /// </summary>
    public static NodeEnginePool Pool => HttpRuntime.WebObjectActivator?.GetService(typeof(NodeEnginePool)) as NodeEnginePool ?? Fallback;

    /// <summary>
    /// The default pool, made the first time it is wanted.
    /// </summary>
    static NodeEnginePool Fallback
    {
        get
        {
            lock (sync)
                return fallback ??= Create();
        }
    }

    /// <summary>
    /// Makes the default pool from the <c>alethic.node</c> section, and has it disposed of when the application shuts
    /// down.
    /// </summary>
    static NodeEnginePool Create()
    {
        var section = (HostingEnvironment.IsHosted
            ? WebConfigurationManager.GetSection(NodeSection.SectionName, HostingEnvironment.ApplicationVirtualPath)
            : ConfigurationManager.GetSection(NodeSection.SectionName)) as NodeSection ?? new NodeSection();

        // What the site's activator supplies, where it has one, for what the pool logs and what its engines are
        // configured with.
        var services = HttpRuntime.WebObjectActivator ?? NoServices.Instance;
        var loggerFactory = services.GetService(typeof(ILoggerFactory)) as ILoggerFactory ?? NullLoggerFactory.Instance;

        var created = new NodeEnginePool(section.ToOptions(), loggerFactory, services);
        if (HostingEnvironment.IsHosted)
            HostingEnvironment.RegisterObject(new Stopper(created));

        return created;
    }

    /// <summary>
    /// Disposes of the default pool when the application shuts down, so no engine's thread calls into the AppDomain as
    /// it unloads.
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
    /// A provider of no services, for a site without an activator.
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
