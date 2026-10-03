using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Node.AspNet.Components.Tests;

/// <summary>
/// What a prop declared in markup is: a JavaScript value, from text read as its type, a bound value with its own,
/// nesting, or nothing.
/// </summary>
[TestClass]
public class ComponentPropTests
{

    /// <summary>
    /// The JSON of the value a prop declares.
    /// </summary>
    /// <param name="prop">The prop.</param>
    static string Json(ComponentProp prop) => prop.ToValue().ToJson();

    /// <summary>
    /// Text is read as its type; left to itself, it is a string.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="type">The type.</param>
    /// <param name="json">What it becomes.</param>
    [TestMethod]
    [DataRow("5", ComponentPropType.Auto, "\"5\"")]
    [DataRow("5", ComponentPropType.String, "\"5\"")]
    [DataRow("5", ComponentPropType.Number, "5")]
    [DataRow("-1.5", ComponentPropType.Number, "-1.5")]
    [DataRow("2e3", ComponentPropType.Number, "2e3")]
    [DataRow("true", ComponentPropType.Boolean, "true")]
    [DataRow("False", ComponentPropType.Boolean, "false")]
    public void Text_is_read_as_its_type(string value, ComponentPropType type, string json)
    {
        Assert.AreEqual(json, Json(new ComponentProp() { Value = value, Type = type }));
    }

    /// <summary>
    /// A bound value keeps its own type, an object or a list becoming an object or an array; a type asked for still
    /// applies.
    /// </summary>
    [TestMethod]
    public void A_bound_value_keeps_its_type()
    {
        Assert.AreEqual("5", Json(new ComponentProp() { Value = 5 }));
        Assert.AreEqual("1.5", Json(new ComponentProp() { Value = 1.5m }));
        Assert.AreEqual("true", Json(new ComponentProp() { Value = true }));
        Assert.AreEqual("""{"lineCount":2}""", Json(new ComponentProp() { Value = new { LineCount = 2 } }));
        Assert.AreEqual("[1,2]", Json(new ComponentProp() { Value = new List<int> { 1, 2 } }));
        Assert.AreEqual("\"5\"", Json(new ComponentProp() { Value = 5, Type = ComponentPropType.String }));
        Assert.AreEqual("5", Json(new ComponentProp() { Value = 5L, Type = ComponentPropType.Number }));
    }

    /// <summary>
    /// Nothing is <c>null</c>, whatever its type, as a bound value is before it is bound.
    /// </summary>
    [TestMethod]
    public void Nothing_is_null()
    {
        Assert.AreEqual("null", Json(new ComponentProp()));
        Assert.AreEqual("null", Json(new ComponentProp() { Type = ComponentPropType.Null }));
        Assert.AreEqual("null", Json(new ComponentProp() { Type = ComponentPropType.Number }));
        Assert.AreEqual("null", Json(new ComponentProp() { Type = ComponentPropType.String }));
    }

    /// <summary>
    /// Nested props with names make an object; without, an array, in order, whose items may themselves be objects.
    /// </summary>
    [TestMethod]
    public void Nesting_makes_an_object_or_an_array()
    {
        var obj = new ComponentProp() { Name = "filter" };
        obj.Controls.Add(new ComponentProp() { Name = "brand", Value = "Eppendorf" });
        obj.Controls.Add(new ComponentCallback() { Name = "onChange", CommandName = "Change" });
        Assert.AreEqual("""{"brand":"Eppendorf","onChange":{"$componentCommand":"Change"}}""", Json(obj));

        var array = new ComponentProp() { Name = "rows" };
        var first = new ComponentProp();
        first.Controls.Add(new ComponentProp() { Name = "sku", Value = "A1" });
        array.Controls.Add(first);
        array.Controls.Add(new ComponentProp() { Value = "2", Type = ComponentPropType.Number });
        array.Controls.Add(new ComponentProp());
        Assert.AreEqual("""[{"sku":"A1"},2,null]""", Json(array));
    }

    /// <summary>
    /// A prop that contradicts itself, or whose value is not its type, fails, naming it.
    /// </summary>
    [TestMethod]
    public void A_prop_declared_wrongly_fails()
    {
        var both = new ComponentProp() { Name = "both", Value = "x" };
        both.Controls.Add(new ComponentProp() { Name = "inner", Value = "y" });
        StringAssert.Contains(Assert.ThrowsExactly<InvalidOperationException>(() => both.ToValue()).Message, "'both'");

        var nestedNumber = new ComponentProp() { Name = "typed", Type = ComponentPropType.Number };
        nestedNumber.Controls.Add(new ComponentProp() { Name = "inner", Value = "y" });
        Assert.ThrowsExactly<InvalidOperationException>(() => nestedNumber.ToValue());

        var mixed = new ComponentProp() { Name = "mixed" };
        mixed.Controls.Add(new ComponentProp() { Name = "named", Value = "x" });
        mixed.Controls.Add(new ComponentProp() { Value = "y" });
        StringAssert.Contains(Assert.ThrowsExactly<InvalidOperationException>(() => mixed.ToValue()).Message, "'mixed'");

        Assert.ThrowsExactly<InvalidOperationException>(() => new ComponentProp() { Name = "nan", Value = "NaN", Type = ComponentPropType.Number }.ToValue());
        Assert.ThrowsExactly<InvalidOperationException>(() => new ComponentProp() { Name = "word", Value = "yes", Type = ComponentPropType.Boolean }.ToValue());
        Assert.ThrowsExactly<InvalidOperationException>(() => new ComponentProp() { Name = "text", Value = "x", Type = ComponentPropType.Null }.ToValue());

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => new ComponentProp() { Name = "count", Value = "many", Type = ComponentPropType.Number }.ToValue());
        StringAssert.Contains(thrown.Message, "'count'");
    }

}
