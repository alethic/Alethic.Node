using System;
using System.Collections.Generic;
using System.Linq;

namespace Sample.WebForms;

/// <summary>
/// The products the demo sells.
/// </summary>
public static class Catalog
{

    static readonly Product[] products =
    [
        new("PIP-100", "Pipette, 100 µL", 189.00m),
        new("PIP-1000", "Pipette, 1000 µL", 199.00m),
        new("TIP-200", "Filter tips, 200 µL, rack of 96", 14.50m),
        new("TUB-15", "Conical tubes, 15 mL, pack of 50", 22.75m),
        new("GLV-M", "Nitrile gloves, medium, box of 100", 11.25m),
        new("BKR-250", "Beaker, borosilicate, 250 mL", 8.40m),
    ];

    /// <summary>
    /// The products whose name or SKU holds a term; all of them where there is none.
    /// </summary>
    /// <param name="term">The term.</param>
    public static IEnumerable<Product> Search(string? term)
    {
        return string.IsNullOrWhiteSpace(term)
            ? products
            : products.Where(i => i.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || i.Sku.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
    }

}
