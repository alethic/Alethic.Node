namespace Alethic.Node.AspNet.Components;

/// <summary>
/// The JavaScript type a <see cref="ComponentProp"/>'s value is read as.
/// </summary>
/// <remarks>
/// Scalars only: an object or an array is not a type a value is read as, but the props nested inside a prop, an object
/// where they have names and an array where they have none.
/// </remarks>
public enum ComponentPropType
{

    /// <summary>
    /// As it is: text written in markup is a string, and a bound value keeps its own type.
    /// </summary>
    Auto,

    /// <summary>
    /// A string.
    /// </summary>
    String,

    /// <summary>
    /// A number: text written as JSON writes one, or a bound numeric value.
    /// </summary>
    Number,

    /// <summary>
    /// <c>true</c> or <c>false</c>.
    /// </summary>
    Boolean,

    /// <summary>
    /// <c>null</c>: the prop declares no value.
    /// </summary>
    Null,

}
