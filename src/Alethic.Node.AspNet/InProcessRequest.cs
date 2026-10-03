using System;
using System.Diagnostics;
using System.IO;
using System.Web;
using System.Web.SessionState;

namespace Alethic.Node.AspNet;

/// <summary>
/// Answers a <c>GET</c> of the site from inside one of its own requests, as the visitor that request is for.
/// </summary>
/// <remarks>
/// Not through <see cref="HttpServerUtility.Execute(IHttpHandler, TextWriter, bool)"/>, which runs pages and nothing
/// else: any other handler is answered 404 without being called. The handler runs in a context of its own instead, made
/// from the request's — its user, cookies and session, with the URL and query string asked for — and a response of its
/// own, whose output is the answer. <see cref="HttpContext.Current"/> is that context while the handler runs, since
/// handlers and what they call find their request there.
///
/// What a context made this way cannot carry, it does not: the request's headers other than its cookies, a method other
/// than <c>GET</c>, a request body, response headers other than the content type, and output other than text.
/// </remarks>
public static class InProcessRequest
{

    /// <summary>
    /// Answers a <c>GET</c> of a path, by the handler the site maps it to.
    /// </summary>
    /// <param name="context">The request it is asked from.</param>
    /// <param name="pathAndQuery">What was asked for: a path from the server's root, as a URL has it, and its query.</param>
    /// <param name="handlers">Finds the handler for a path relative to the application's root; the site's
    /// <see cref="HandlerMap"/> where <see langword="null"/>.</param>
    /// <returns>The answer: a 404 where nothing handles the path, and the error status a browser would have been given
    /// where the handler threw, with what it threw.</returns>
    public static InProcessResponse Get(HttpContext context, string pathAndQuery, Func<HttpContext, string, IHttpHandler?>? handlers = null)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (pathAndQuery is null)
            throw new ArgumentNullException(nameof(pathAndQuery));

        handlers ??= (child, path) => HandlerMap.Site.Find(child, path);

        var split = pathAndQuery.IndexOf('?');
        var path = split < 0 ? pathAndQuery : pathAndQuery.Substring(0, split);
        var query = split < 0 ? "" : pathAndQuery.Substring(split + 1);
        var url = new Uri(context.Request.Url, pathAndQuery);

        using var writer = new StringWriter();
        var request = new HttpRequest("", url.AbsoluteUri, query);
        foreach (string name in context.Request.Cookies)
            request.Cookies.Add(context.Request.Cookies[name]);

        var response = new HttpResponse(writer);
        var child = new HttpContext(request, response) { User = context.User };

        // Asked only where there is a session: with none, it throws rather than answer null.
        if (context.Session is not null && SessionStateUtility.GetHttpSessionStateFromContext(context) is IHttpSessionState session)
            SessionStateUtility.AddHttpSessionStateToContext(child, session);

        var previous = HttpContext.Current;
        HttpContext.Current = child;
        try
        {
            var handler = handlers(child, ApplicationRelative(path));
            if (handler is null)
                return new InProcessResponse(404, null, "");

            handler.ProcessRequest(child);
            return new InProcessResponse(response.StatusCode, response.ContentType, writer.ToString());
        }
        catch (Exception e)
        {
            // Answered as the browser would be: a failure is the response's, for whoever asked to handle.
            Trace.TraceWarning("Alethic.Node.AspNet: {0} failed in process: {1}", pathAndQuery, e);
            return new InProcessResponse(e is HttpException http ? http.GetHttpCode() : 500, null, "", e);
        }
        finally
        {
            HttpContext.Current = previous;
        }
    }

    /// <summary>
    /// A path from the server's root as a path from the application's, without its leading slash.
    /// </summary>
    /// <param name="path">The path from the server's root.</param>
    static string ApplicationRelative(string path)
    {
        var root = HttpRuntime.AppDomainAppVirtualPath ?? "/";
        if (root != "/" && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && (path.Length == root.Length || path[root.Length] == '/'))
            path = path.Substring(root.Length);

        return path.TrimStart('/');
    }

}
