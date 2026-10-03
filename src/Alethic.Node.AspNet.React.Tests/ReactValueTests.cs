using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.AspNet.React.Tests;

/// <summary>
/// Props as a tree of values: what JSON carries, plus callbacks, written to JSON and read back.
/// </summary>
[TestClass]
public class ReactValueTests
{

    /// <summary>
    /// Props round-trip through JSON, callbacks included, wherever they are.
    /// </summary>
    [TestMethod]
    public void Props_round_trip_through_json()
    {
        var props = new ReactObject
        {
            { "title", "Pipettes" },
            { "count", 3 },
            { "on", true },
            { "nothing", null },
            { "onSelect", new ReactCommand("Select") },
            { "items", new ReactArray { "a", new ReactObject { { "onPick", new ReactCommand("Pick") } } } },
        };

        var json = props.ToJson();
        Assert.AreEqual("""{"title":"Pipettes","count":3,"on":true,"nothing":null,"onSelect":{"$reactCommand":"Select"},"items":["a",{"onPick":{"$reactCommand":"Pick"}}]}""", json);
        Assert.AreEqual(json, ReactValue.Parse(json).ToJson());

        var read = (ReactObject)ReactValue.Parse(json);
        Assert.AreEqual("Select", ((ReactCommand)read["onSelect"]!).CommandName);
        Assert.AreEqual("Pick", ((ReactCommand)read["items"]![1]!["onPick"]!).CommandName);
        Assert.AreEqual(3, ((ReactScalar)read["count"]!).Get<int>());
    }

    /// <summary>
    /// An object that only looks like a command, by having more keys than one, is an object.
    /// </summary>
    [TestMethod]
    public void Only_a_lone_command_key_is_a_command()
    {
        var read = ReactValue.Parse("""{"$reactCommand":"Select","other":1}""");
        Assert.IsInstanceOfType<ReactObject>(read);
    }

    /// <summary>
    /// Any object is props as JSON makes it, camel-cased.
    /// </summary>
    [TestMethod]
    public void An_object_is_props_camel_cased()
    {
        var value = ReactValue.FromObject(new { ProductName = "Pipette", Sizes = new[] { 1, 2 } });
        Assert.AreEqual("""{"productName":"Pipette","sizes":[1,2]}""", value.ToJson());
    }

    /// <summary>
    /// Keys keep the order they were set in; setting <see langword="null"/> sets <c>null</c>, and removing takes the key
    /// away.
    /// </summary>
    [TestMethod]
    public void Keys_keep_their_order_and_null_is_a_value()
    {
        var props = new ReactObject { { "b", 1 }, { "a", 2 } };
        props["b"] = null;
        props["c"] = 3;
        Assert.AreEqual("""{"b":null,"a":2,"c":3}""", props.ToJson());

        Assert.IsTrue(props.Remove("a"));
        Assert.AreEqual("""{"b":null,"c":3}""", props.ToJson());
        Assert.IsNull(props["a"]);
    }

}
