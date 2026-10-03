using System;
using System.Collections.Generic;

namespace Sample.WebForms;

/// <summary>
/// An order, as a page might bind one.
/// </summary>
/// <param name="lines">How many lines it has.</param>
/// <param name="total">Its total.</param>
/// <param name="placed">When it was placed.</param>
/// <param name="tags">Its tags.</param>
/// <param name="shipTo">Where it ships.</param>
public sealed class OrderSummary(int lines, decimal total, DateTime placed, List<string> tags, Address shipTo)
{

    /// <summary>
    /// How many lines it has.
    /// </summary>
    public int Lines { get; } = lines;

    /// <summary>
    /// Its total.
    /// </summary>
    public decimal Total { get; } = total;

    /// <summary>
    /// When it was placed.
    /// </summary>
    public DateTime Placed { get; } = placed;

    /// <summary>
    /// Its tags.
    /// </summary>
    public List<string> Tags { get; } = tags;

    /// <summary>
    /// Where it ships.
    /// </summary>
    public Address ShipTo { get; } = shipTo;

}
