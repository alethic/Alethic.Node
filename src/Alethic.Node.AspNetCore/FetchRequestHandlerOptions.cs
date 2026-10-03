using Alethic.Node.Http;

namespace Alethic.Node.AspNetCore;

/// <summary>
/// Configures a <see cref="FetchRequestHandler"/>.
/// </summary>
public class FetchRequestHandlerOptions : FetchProtocolOptions
{

    /// <summary>
    /// The application's server module: a self-contained CommonJS bundle.
    /// </summary>
    public NodeModuleSource? Module { get; set; }

    /// <summary>
    /// How the request body reaches the application. Streamed by default.
    /// </summary>
    /// <remarks>
    /// Buffered where the application needs to read the body more than once, which a stream does not
    /// allow: cloning a request, or retrying a parse. It costs the body in memory.
    /// </remarks>
    public BodyMode RequestBody { get; set; } = BodyMode.Streamed;

}
