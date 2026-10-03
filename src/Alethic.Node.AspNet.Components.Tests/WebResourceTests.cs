using System.Linq;
using System.Reflection;
using System.Web.UI;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.AspNet.Components.Tests;

/// <summary>
/// The script that places the components, as ASP.NET serves it.
/// </summary>
[TestClass]
public class WebResourceTests
{

    /// <summary>
    /// The script is embedded in the assembly, and declared as a web resource, without which <c>WebResource.axd</c>
    /// refuses to serve it.
    /// </summary>
    [TestMethod]
    public void The_script_is_an_embedded_web_resource()
    {
        var assembly = typeof(Component).Assembly;

        CollectionAssert.Contains(assembly.GetManifestResourceNames(), Component.ScriptResource);
        Assert.IsTrue(assembly.GetCustomAttributes<WebResourceAttribute>().Any(i => i.WebResource == Component.ScriptResource && i.ContentType == "text/javascript"));
    }

}
