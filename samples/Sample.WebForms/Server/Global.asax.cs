using System;
using System.Web;
using System.Web.Hosting;
using System.Web.Routing;

using Alethic.Node;
using Alethic.Node.AspNet;

namespace Sample.WebForms;

/// <summary>
/// The site.
/// </summary>
public class Global : HttpApplication
{

    /// <summary>
    /// Mounts the shared client's full-page application on its routes, beside the site's pages: the ASP.NET Core
    /// sample's <c>fetch</c> handler, which renders whole documents.
    /// </summary>
    /// <param name="sender">The application.</param>
    /// <param name="e">The event's arguments.</param>
    protected void Application_Start(object sender, EventArgs e)
    {
        var app = new FetchRequestHandler(NodeModuleSource.FromFile(HostingEnvironment.MapPath("~/App_Data/app/app.cjs")));

        // Not its home page, "/": that is the site's own Default.aspx.
        RouteTable.Routes.MapNode("about", app);
        RouteTable.Routes.MapNode("parks/{parkRef}", app);
    }

}
