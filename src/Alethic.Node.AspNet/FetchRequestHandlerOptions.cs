using Alethic.Node.Http;

namespace Alethic.Node.AspNet;

/// <summary>
/// Configures a <see cref="FetchRequestHandler"/>.
/// </summary>
public class FetchRequestHandlerOptions : FetchProtocolOptions
{

    /// <summary>
    /// The pool the application runs on; the application's pool, <see cref="AspNetNode.Pool"/>, where
    /// <see langword="null"/>.
    /// </summary>
    public NodeEnginePool? Pool { get; set; }

}
