using System;
using System.Text.Json;
using System.Web;
using System.Web.SessionState;

namespace Sample.WebForms;

/// <summary>
/// The site's time, and who asked: what a component fetches. Rendered on the server, the fetch is answered here
/// in process, as the visitor whose page is rendering, with their session.
/// </summary>
public class Time : IHttpHandler, IRequiresSessionState
{

    /// <summary>
    /// Whether one instance serves several requests.
    /// </summary>
    public bool IsReusable => true;

    /// <summary>
    /// Answers with the time, the visitor's session, and how often they have asked.
    /// </summary>
    /// <param name="context">The request.</param>
    public void ProcessRequest(HttpContext context)
    {
        var visits = (context.Session?["visits"] as int? ?? 0) + 1;
        if (context.Session is not null)
            context.Session["visits"] = visits;

        context.Response.ContentType = "application/json";
        context.Response.Write(JsonSerializer.Serialize(new
        {
            now = DateTimeOffset.Now.ToString("u"),
            visitor = context.Session?.SessionID,
            visits,
        }));
    }

}
