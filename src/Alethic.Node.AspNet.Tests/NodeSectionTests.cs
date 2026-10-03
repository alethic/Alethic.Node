using System;
using System.Configuration;
using System.Web;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.AspNet.Tests;

/// <summary>
/// The <c>alethic.node</c> section, and the default pool made from it where no activator supplies one.
/// </summary>
[TestClass]
public class NodeSectionTests
{

    /// <summary>
    /// A section with nothing set gives every default.
    /// </summary>
    [TestMethod]
    public void An_empty_section_gives_the_defaults()
    {
        var options = new NodeSection().ToOptions();

        Assert.AreEqual(1, options.EngineCount);
        Assert.AreEqual(4, options.MaxConcurrencyPerEngine);
        Assert.AreEqual(TimeSpan.FromSeconds(10), options.AcquireTimeout);
        Assert.IsNull(options.LibNodePath);
        Assert.IsNull(options.BaseDirectory);
    }

    /// <summary>
    /// What the configuration file sets, the section reads.
    /// </summary>
    [TestMethod]
    public void The_section_reads_the_configuration_file()
    {
        var section = (NodeSection)ConfigurationManager.GetSection(NodeSection.SectionName);
        var options = section.ToOptions();

        Assert.AreEqual(3, options.EngineCount);
        Assert.AreEqual(2, options.MaxConcurrencyPerEngine);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.AcquireTimeout);
    }

    /// <summary>
    /// Without an activator, or with one that supplies no pool, the application's pool is the default, made once.
    /// </summary>
    [TestMethod]
    public void Without_an_activator_the_pool_is_the_default()
    {
        var previous = HttpRuntime.WebObjectActivator;
        try
        {
            HttpRuntime.WebObjectActivator = null;
            var first = AspNetNode.Pool;

            HttpRuntime.WebObjectActivator = new SuppliesNothing();
            Assert.AreSame(first, AspNetNode.Pool);
        }
        finally
        {
            HttpRuntime.WebObjectActivator = previous;
        }
    }

    /// <summary>
    /// An activator that supplies nothing.
    /// </summary>
    sealed class SuppliesNothing : IServiceProvider
    {

        /// <summary>
        /// Provides nothing.
        /// </summary>
        /// <param name="serviceType">The service asked for.</param>
        public object? GetService(Type serviceType) => null;

    }

}
