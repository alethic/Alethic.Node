using System;
using System.IO;
using System.Web;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.AspNet.Tests;

/// <summary>
/// A request of the site answered from inside another, without Node.
/// </summary>
[TestClass]
public class InProcessRequestTests
{

    /// <summary>
    /// A request for a page of the site, outside any pipeline: no session, no hosting environment.
    /// </summary>
    static HttpContext Context()
    {
        return new HttpContext(new HttpRequest("", "http://site.test/page.aspx", ""), new HttpResponse(TextWriter.Null));
    }

    /// <summary>
    /// A path nothing handles is a 404, from a context without a session.
    /// </summary>
    [TestMethod]
    public void A_path_nothing_handles_is_404()
    {
        var answer = InProcessRequest.Get(Context(), "/thing?x=1", (context, path) => null);

        Assert.AreEqual(404, answer.StatusCode);
        Assert.AreEqual("", answer.Body);
        Assert.IsNull(answer.Exception);
    }

    /// <summary>
    /// The handler is asked for by the path relative to the application, and the context it runs in is current only
    /// while it runs.
    /// </summary>
    [TestMethod]
    public void The_handler_context_is_current_only_while_it_runs()
    {
        var parent = Context();
        HttpContext.Current = parent;

        try
        {
            string? asked = null;
            var answer = InProcessRequest.Get(parent, "/a/b?c=d", (context, path) =>
            {
                asked = path;
                return null;
            });

            Assert.AreEqual("a/b", asked);
            Assert.AreSame(parent, HttpContext.Current);
        }
        finally
        {
            HttpContext.Current = null;
        }
    }

    /// <summary>
    /// What a handler throws is on the answer, whose status is the one a browser would have been given.
    /// </summary>
    [TestMethod]
    public void What_a_handler_throws_is_on_the_answer()
    {
        var thrown = new InvalidOperationException("failed");
        var answer = InProcessRequest.Get(Context(), "/x", (context, path) => throw thrown);

        Assert.AreEqual(500, answer.StatusCode);
        Assert.AreSame(thrown, answer.Exception);
    }

}
