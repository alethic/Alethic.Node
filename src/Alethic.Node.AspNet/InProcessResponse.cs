using System;

namespace Alethic.Node.AspNet;

/// <summary>
/// What the site answered a request made of it in process.
/// </summary>
public sealed class InProcessResponse
{

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="contentType">The content type, where there is one.</param>
    /// <param name="body">The body, as text.</param>
    /// <param name="exception">What the handler threw, where it threw.</param>
    public InProcessResponse(int statusCode, string? contentType, string body, Exception? exception = null)
    {
        StatusCode = statusCode;
        ContentType = contentType;
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Exception = exception;
    }

    /// <summary>
    /// The status code.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// The content type, where there is one.
    /// </summary>
    public string? ContentType { get; }

    /// <summary>
    /// The body, as text.
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// What the handler threw, where it threw: the response is then the error status a browser would have been given.
    /// </summary>
    public Exception? Exception { get; }

}
