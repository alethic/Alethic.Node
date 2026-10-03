namespace Alethic.Node.AspNet;

/// <summary>
/// How a response body crosses from the application to the client.
/// </summary>
public enum BodyMode
{

    /// <summary>
    /// Written in pieces, as the application produces them, each flushed to the client.
    /// </summary>
    /// <remarks>
    /// What is in memory at once is a chunk rather than a page, and progress the render makes is progress the client
    /// sees, which is what lets a shell reach a browser ahead of the content suspended behind it. A failure partway
    /// through can only truncate the page: the status has already gone out.
    /// </remarks>
    Streamed,

    /// <summary>
    /// Collected whole before any of it is written.
    /// </summary>
    /// <remarks>
    /// A failure partway through is still a failure, which the site's error handling answers, since nothing has been
    /// written. The response carries a length rather than chunked framing. The cost is the whole page in memory, and
    /// nothing reaching the client until all of it exists.
    /// </remarks>
    Buffered,

}
