using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Threading;
using System.Web;

namespace Alethic.Node.AspNet.Tests;

/// <summary>
/// A response that keeps what is written to it, for a handler run outside IIS.
/// </summary>
sealed class RecordingResponse : HttpResponseBase
{

    /// <summary>
    /// The headers appended, other than the content type.
    /// </summary>
    public NameValueCollection Appended { get; } = new();

    /// <summary>
    /// How many times the response was flushed.
    /// </summary>
    public int Flushes { get; private set; }

    /// <summary>
    /// What was written, as text.
    /// </summary>
    public string Body => Encoding.UTF8.GetString(Output.ToArray());

    /// <summary>
    /// What was written.
    /// </summary>
    public MemoryStream Output { get; } = new();

    /// <summary>
    /// Cancelled to say the visitor has gone.
    /// </summary>
    public CancellationTokenSource Disconnect { get; } = new();

    /// <summary>
    /// The status.
    /// </summary>
    public override int StatusCode { get; set; } = 200;

    /// <summary>
    /// The status's text.
    /// </summary>
    public override string StatusDescription { get; set; } = "OK";

    /// <summary>
    /// The content type, without its charset.
    /// </summary>
    public override string ContentType { get; set; } = "text/html";

    /// <summary>
    /// The charset.
    /// </summary>
    public override string Charset { get; set; } = "utf-8";

    /// <summary>
    /// Whether output is held until the response ends.
    /// </summary>
    public override bool BufferOutput { get; set; } = true;

    /// <summary>
    /// Whether IIS's own error pages are kept out.
    /// </summary>
    public override bool TrySkipIisCustomErrors { get; set; }

    /// <summary>
    /// Where the body is written.
    /// </summary>
    public override Stream OutputStream => Output;

    /// <summary>
    /// Cancelled when the visitor goes.
    /// </summary>
    public override CancellationToken ClientDisconnectedToken => Disconnect.Token;

    /// <summary>
    /// Appends a header.
    /// </summary>
    /// <param name="name">Its name.</param>
    /// <param name="value">Its value.</param>
    public override void AppendHeader(string name, string value)
    {
        Appended.Add(name, value);
    }

    /// <summary>
    /// Counts a flush.
    /// </summary>
    public override void Flush()
    {
        Flushes++;
    }

}
