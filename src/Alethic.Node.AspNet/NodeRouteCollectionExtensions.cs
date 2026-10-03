using System;
using System.Web;
using System.Web.Routing;

namespace Alethic.Node.AspNet;

/// <summary>
/// Mounts a Node application on the site's routes.
/// </summary>
/// <remarks>
/// A request no route matches goes on to the site's pages and handlers, as it would without these, and so does one
/// for a file that exists, unless <see cref="RouteCollection.RouteExistingFiles"/> says otherwise: the application and
/// the site share the URL space.
/// </remarks>
public static class NodeRouteCollectionExtensions
{

    /// <summary>
    /// Mounts an application's <c>fetch</c> handler on a route.
    /// </summary>
    /// <param name="routes">The site's routes, <see cref="RouteTable.Routes"/>.</param>
    /// <param name="url">The route's URL pattern, such as <c>parks/{parkRef}</c>.</param>
    /// <param name="module">The application's server module.</param>
    /// <returns>The route, for its defaults and constraints.</returns>
    public static Route MapNode(this RouteCollection routes, string url, NodeModuleSource module)
    {
        return MapNode(routes, url, new FetchRequestHandler(module));
    }

    /// <summary>
    /// Mounts a handler on a route.
    /// </summary>
    /// <param name="routes">The site's routes, <see cref="RouteTable.Routes"/>.</param>
    /// <param name="url">The route's URL pattern, such as <c>parks/{parkRef}</c>.</param>
    /// <param name="handler">The handler, such as a <see cref="FetchRequestHandler"/>.</param>
    /// <returns>The route, for its defaults and constraints.</returns>
    public static Route MapNode(this RouteCollection routes, string url, IHttpHandler handler)
    {
        return MapNode(routes, null, url, handler);
    }

    /// <summary>
    /// Mounts a handler on a named route.
    /// </summary>
    /// <param name="routes">The site's routes, <see cref="RouteTable.Routes"/>.</param>
    /// <param name="name">The route's name, for generating its URLs; none where <see langword="null"/>.</param>
    /// <param name="url">The route's URL pattern, such as <c>parks/{parkRef}</c>.</param>
    /// <param name="handler">The handler, such as a <see cref="FetchRequestHandler"/>.</param>
    /// <returns>The route, for its defaults and constraints.</returns>
    public static Route MapNode(this RouteCollection routes, string? name, string url, IHttpHandler handler)
    {
        if (routes is null)
            throw new ArgumentNullException(nameof(routes));
        if (url is null)
            throw new ArgumentNullException(nameof(url));

        var route = new Route(url, new NodeRouteHandler(handler));

        if (name is null)
            routes.Add(route);
        else
            routes.Add(name, route);

        return route;
    }

}
