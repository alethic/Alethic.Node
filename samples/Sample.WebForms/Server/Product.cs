namespace Sample.WebForms;

/// <summary>
/// A product: bound to a component's prop, it reaches JavaScript as <c>{ sku, name, price }</c>.
/// </summary>
/// <param name="sku">Its SKU.</param>
/// <param name="name">Its name.</param>
/// <param name="price">Its price.</param>
public sealed class Product(string sku, string name, decimal price)
{

    /// <summary>
    /// Its SKU.
    /// </summary>
    public string Sku { get; } = sku;

    /// <summary>
    /// Its name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Its price.
    /// </summary>
    public decimal Price { get; } = price;

}
