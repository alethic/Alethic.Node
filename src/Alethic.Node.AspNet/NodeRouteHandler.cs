using System;
using System.Web;
using System.Web.Routing;

namespace Alethic.Node.AspNet;

/// <summary>
/// Hands a route's requests to a handler that answers them from a Node application, such as a
/// <see cref="FetchRequestHandler"/>.
/// </summary>
public sealed class NodeRouteHandler : IRouteHandler
{

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="handler">The handler the route's requests go to.</param>
    public NodeRouteHandler(IHttpHandler handler)
    {
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// The handler the route's requests go to.
    /// </summary>
    public IHttpHandler Handler { get; }

    /// <summary>
    /// The handler for a request the route matched.
    /// </summary>
    /// <param name="requestContext">The request and its route.</param>
    public IHttpHandler GetHttpHandler(RequestContext requestContext)
    {
        return Handler;
    }

}
