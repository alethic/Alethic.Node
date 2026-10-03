using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.AspNet.Components.Tests;

/// <summary>
/// Props as a tree of values: what JSON carries, plus callbacks, written to JSON and read back.
/// </summary>
[TestClass]
public class ComponentValueTests
{

    /// <summary>
    /// Props round-trip through JSON, callbacks included, wherever they are.
    /// </summary>
    [TestMethod]
    public void Props_round_trip_through_json()
    {
        var props = new ComponentObject
        {
            { "title", "Pipettes" },
            { "count", 3 },
            { "on", true },
            { "nothing", null },
            { "onSelect", new ComponentCommand("Select") },
            { "items", new ComponentArray { "a", new ComponentObject { { "onPick", new ComponentCommand("Pick") } } } },
        };

        var json = props.ToJson();
        Assert.AreEqual("""{"title":"Pipettes","count":3,"on":true,"nothing":null,"onSelect":{"$componentCommand":"Select"},"items":["a",{"onPick":{"$componentCommand":"Pick"}}]}""", json);
        Assert.AreEqual(json, ComponentValue.Parse(json).ToJson());

        var read = (ComponentObject)ComponentValue.Parse(json);
        Assert.AreEqual("Select", ((ComponentCommand)read["onSelect"]!).CommandName);
        Assert.AreEqual("Pick", ((ComponentCommand)read["items"]![1]!["onPick"]!).CommandName);
        Assert.AreEqual(3, ((ComponentScalar)read["count"]!).Get<int>());
    }

    /// <summary>
    /// An object that only looks like a command, by having more keys than one, is an object.
    /// </summary>
    [TestMethod]
    public void Only_a_lone_command_key_is_a_command()
    {
        var read = ComponentValue.Parse("""{"$componentCommand":"Select","other":1}""");
        Assert.IsInstanceOfType<ComponentObject>(read);
    }

    /// <summary>
    /// Any object is props as JSON makes it, camel-cased.
    /// </summary>
    [TestMethod]
    public void An_object_is_props_camel_cased()
    {
        var value = ComponentValue.FromObject(new { ProductName = "Pipette", Sizes = new[] { 1, 2 } });
        Assert.AreEqual("""{"productName":"Pipette","sizes":[1,2]}""", value.ToJson());
    }

    /// <summary>
    /// Keys keep the order they were set in; setting <see langword="null"/> sets <c>null</c>, and removing takes the key
    /// away.
    /// </summary>
    [TestMethod]
    public void Keys_keep_their_order_and_null_is_a_value()
    {
        var props = new ComponentObject { { "b", 1 }, { "a", 2 } };
        props["b"] = null;
        props["c"] = 3;
        Assert.AreEqual("""{"b":null,"a":2,"c":3}""", props.ToJson());

        Assert.IsTrue(props.Remove("a"));
        Assert.AreEqual("""{"b":null,"c":3}""", props.ToJson());
        Assert.IsNull(props["a"]);
    }

}
