using System;
using System.Collections.Generic;

namespace Alethic.Node.AspNet;

/// <summary>
/// Configures a <see cref="FetchRequestHandler"/>.
/// </summary>
public class FetchRequestHandlerOptions
{

    /// <summary>
    /// The pool the application runs on; the application's pool, <see cref="AspNetNode.Pool"/>, where
    /// <see langword="null"/>.
    /// </summary>
    public NodeEnginePool? Pool { get; set; }

    /// <summary>
    /// The address the application is asked at, which the path below the site's root is resolved against.
    /// </summary>
    /// <remarks>
    /// Not where the visitor was, and deliberately not able to pass for it: the default authority is reserved by RFC
    /// 2606 against ever resolving. Where the visitor was is told in <c>X-Forwarded-Proto</c>, <c>X-Forwarded-Host</c>
    /// and <c>X-Forwarded-Prefix</c>, which the application reads.
    ///
    /// A path here is put ahead of the request's own, for an application that expects to be asked under one.
    /// </remarks>
    public Uri BaseUri { get; set; } = new Uri("http://node.invalid/");

    /// <summary>
    /// How the response reaches the client. Streamed unless set.
    /// </summary>
    public BodyMode ResponseBody { get; set; } = BodyMode.Streamed;

    /// <summary>
    /// Values the site gives the application, as the <c>env</c> argument of <c>fetch(request, env, ctx)</c>: what only
    /// the site knows, such as an internal address or an environment's name.
    /// </summary>
    /// <remarks>
    /// Made afresh for each request, so the application keeps its own state in module scope rather than on it.
    /// </remarks>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>();

}
