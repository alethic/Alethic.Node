using System.Xml.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.AspNet.Tests;

/// <summary>
/// The handler a path maps to, as a site's <c>system.webServer/handlers</c> declares it.
/// </summary>
[TestClass]
public class HandlerMapTests
{

    /// <summary>
    /// A map of the given handler elements.
    /// </summary>
    /// <param name="handlers">The elements inside <c>handlers</c>.</param>
    static HandlerMap Map(string handlers)
    {
        return HandlerMap.Parse(XDocument.Parse($"<configuration><system.webServer><handlers>{handlers}</handlers></system.webServer></configuration>"));
    }

    /// <summary>
    /// An exact path matches the path's last segment, wherever it is, as IIS matches a path without a slash.
    /// </summary>
    [TestMethod]
    public void An_exact_path_matches_the_last_segment()
    {
        var map = Map("""<add name="a" path="Greeting.axd" verb="*" type="A" />""");

        Assert.AreEqual("A", map.FindType("/Greeting.axd"));
        Assert.AreEqual("A", map.FindType("deep/Greeting.axd"));
        Assert.IsNull(map.FindType("Other.axd"));
    }

    /// <summary>
    /// A path with a slash in it matches the whole path.
    /// </summary>
    [TestMethod]
    public void A_path_with_a_slash_matches_the_whole_path()
    {
        var map = Map("""<add name="a" path="api/thing" verb="*" type="A" />""");

        Assert.AreEqual("A", map.FindType("/api/thing"));
        Assert.IsNull(map.FindType("other/api/thing"));
    }

    /// <summary>
    /// An extension matches any path ending in it, in any case.
    /// </summary>
    [TestMethod]
    public void An_extension_matches_any_path_ending_in_it()
    {
        var map = Map("""<add name="a" path="*.feed" verb="GET" type="A" />""");

        Assert.AreEqual("A", map.FindType("x/y.FEED"));
        Assert.IsNull(map.FindType("x/y.feeds"));
    }

    /// <summary>
    /// The first mapping that matches wins.
    /// </summary>
    [TestMethod]
    public void The_first_match_wins()
    {
        var map = Map("""
            <add name="a" path="*.feed" verb="*" type="A" />
            <add name="b" path="*" verb="*" type="B" />
            """);

        Assert.AreEqual("A", map.FindType("x.feed"));
        Assert.AreEqual("B", map.FindType("x.other"));
    }

    /// <summary>
    /// A mapping that does not take <c>GET</c> does not answer one.
    /// </summary>
    [TestMethod]
    public void A_mapping_without_GET_is_passed_over()
    {
        var map = Map("""
            <add name="a" path="*.feed" verb="POST,PUT" type="A" />
            <add name="b" path="*.feed" verb="HEAD, get" type="B" />
            """);

        Assert.AreEqual("B", map.FindType("x.feed"));
    }

    /// <summary>
    /// <c>remove</c> drops a mapping by name, and <c>clear</c> drops every one before it.
    /// </summary>
    [TestMethod]
    public void Remove_and_clear_drop_mappings()
    {
        var map = Map("""
            <add name="a" path="*.a" verb="*" type="A" />
            <add name="b" path="*.b" verb="*" type="B" />
            <remove name="A" />
            """);

        Assert.IsNull(map.FindType("x.a"));
        Assert.AreEqual("B", map.FindType("x.b"));

        map = Map("""
            <add name="a" path="*.a" verb="*" type="A" />
            <clear />
            <add name="b" path="*.b" verb="*" type="B" />
            """);

        Assert.IsNull(map.FindType("x.a"));
        Assert.AreEqual("B", map.FindType("x.b"));
    }

    /// <summary>
    /// A mapping with no type, a module or a script map, has nothing to run, and is passed over.
    /// </summary>
    [TestMethod]
    public void A_mapping_without_a_type_is_passed_over()
    {
        var map = Map("""
            <add name="a" path="*" verb="*" modules="StaticFileModule" />
            <add name="b" path="*" verb="*" type="B" />
            """);

        Assert.AreEqual("B", map.FindType("x"));
    }

}
