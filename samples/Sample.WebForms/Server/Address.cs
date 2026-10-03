namespace Sample.WebForms;

/// <summary>
/// An address.
/// </summary>
/// <param name="street">Its street.</param>
/// <param name="city">Its city.</param>
/// <param name="state">Its state.</param>
public sealed class Address(string street, string city, string state)
{

    /// <summary>
    /// Its street.
    /// </summary>
    public string Street { get; } = street;

    /// <summary>
    /// Its city.
    /// </summary>
    public string City { get; } = city;

    /// <summary>
    /// Its state.
    /// </summary>
    public string State { get; } = state;

}
