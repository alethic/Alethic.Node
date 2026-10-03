using System;
using System.Collections.Generic;

namespace Alethic.Node.Http;

/// <summary>
/// What every host serving an application's <c>fetch</c> handler is configured with. Each host's options extend it.
/// </summary>
public class FetchProtocolOptions
{

    /// <summary>
    /// The address the application is asked at, which the path below the mount is resolved against.
    /// </summary>
    /// <remarks>
    /// Not where the caller was, and deliberately not able to pass for it. The default authority is reserved by RFC
    /// 2606 against ever resolving, so no deployment can make it look plausible and nothing can quietly take a request
    /// URL for its own public address: <c>X-Forwarded-Proto</c>, <c>X-Forwarded-Host</c> and <c>X-Forwarded-Prefix</c>
    /// are the account of that, and an application is meant to have to read them.
    ///
    /// A path here is inserted ahead of the request's own, for an application that expects to be asked under one. It
    /// is unrelated to where the host has mounted the application, which is removed from the path and named in
    /// <c>X-Forwarded-Prefix</c> instead.
    /// </remarks>
    public Uri BaseUri { get; set; } = FetchProtocol.DefaultBaseUri;

    /// <summary>
    /// How the response reaches the client. Streamed unless set.
    /// </summary>
    /// <remarks>
    /// Buffered where a render that fails partway through should fail rather than truncate: nothing is written until
    /// it has finished, so the status is still open when the fault arrives. It also gives the response a length
    /// instead of chunked framing. A render that waits on all its data before answering gives up nothing by it.
    /// </remarks>
    public BodyMode ResponseBody { get; set; } = BodyMode.Streamed;

    /// <summary>
    /// Values the host supplies to the application, reaching it as the <c>env</c> argument of
    /// <c>fetch(request, env, ctx)</c>: what only the host knows, such as an internal API address or an environment's
    /// name.
    /// </summary>
    /// <remarks>
    /// Input only. The object is built fresh for each request, so an application keeps its own state in module scope,
    /// which Node caches per engine, rather than hanging it off <c>env</c>.
    /// </remarks>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>();

}
