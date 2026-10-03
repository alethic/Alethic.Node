using System.Collections.Generic;
using System.Web;

namespace Sample.WebForms;

/// <summary>
/// The visitor's cart, in their session.
/// </summary>
public static class Cart
{

    const string Key = "cart";

    /// <summary>
    /// How many things are in it.
    /// </summary>
    /// <param name="context">The visitor's request.</param>
    public static int Count(HttpContext context)
    {
        return Items(context).Count;
    }

    /// <summary>
    /// Adds a product.
    /// </summary>
    /// <param name="context">The visitor's request.</param>
    /// <param name="sku">The product's SKU.</param>
    /// <returns>How many things are in it now.</returns>
    public static int Add(HttpContext context, string sku)
    {
        var items = Items(context);
        items.Add(sku);
        return items.Count;
    }

    /// <summary>
    /// The SKUs in it.
    /// </summary>
    /// <param name="context">The visitor's request.</param>
    static List<string> Items(HttpContext context)
    {
        if (context.Session[Key] is not List<string> items)
            context.Session[Key] = items = [];

        return items;
    }

}
