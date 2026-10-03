using System;
using System.Collections.Generic;

namespace Alethic.Node.Http;

/// <summary>
/// The part of an application's <c>Response</c> known before its body has arrived.
/// </summary>
public sealed class FetchResponseHead
{

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="statusText">The status's text, which may be empty.</param>
    /// <param name="headers">The headers, each value of a repeated header on its own.</param>
    public FetchResponseHead(int status, string statusText, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        Status = status;
        StatusText = statusText ?? "";
        Headers = headers ?? throw new ArgumentNullException(nameof(headers));
    }

    /// <summary>
    /// The status.
    /// </summary>
    public int Status { get; }

    /// <summary>
    /// The status's text, which may be empty.
    /// </summary>
    public string StatusText { get; }

    /// <summary>
    /// The headers, each value of a repeated header on its own. The framing headers are among them, for the host to
    /// leave out: see <see cref="FetchProtocol.IsFramingHeader"/>.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

}
