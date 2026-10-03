using System;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

using Alethic.Node.AspNet.Components;

namespace Sample.WebForms;

/// <summary>
/// A catalog of products in a <see cref="Repeater"/>, each a component, and a cart the header's badge shares.
/// </summary>
public partial class DemoPage : Page
{

    /// <summary>
    /// Binds the catalog the first time.
    /// </summary>
    /// <param name="sender">The page.</param>
    /// <param name="e">The event's arguments.</param>
    protected void Page_Load(object sender, EventArgs e)
    {
        if (IsPostBack == false)
            Bind();
    }

    /// <summary>
    /// Binds the products the search finds.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event's arguments.</param>
    protected void Search_Click(object sender, EventArgs e)
    {
        Bind();
    }

    /// <summary>
    /// Adds a product to the cart, and answers with its new count.
    /// </summary>
    /// <param name="sender">The product's control.</param>
    /// <param name="e">The command.</param>
    protected void Product_Command(object sender, ComponentCommandEventArgs e)
    {
        var sku = e.Argument<string>(0) ?? throw new InvalidOperationException("Add needs a SKU.");
        if (Catalog.Search(sku).Any(i => i.Sku == sku) == false)
            throw new InvalidOperationException($"There is no product {sku}.");

        e.Result = Cart.Add(Context, sku);
    }

    /// <summary>
    /// Hears the command bubble up through the <see cref="Repeater"/>, as a button's command does.
    /// </summary>
    /// <param name="source">The repeater.</param>
    /// <param name="e">The command, as the repeater raises it.</param>
    protected void Products_ItemCommand(object source, RepeaterCommandEventArgs e)
    {
        Log.Text = $"The Repeater saw {e.CommandName} bubble up from item {e.Item.ItemIndex} at {DateTime.Now:T}.";
    }

    /// <summary>
    /// Binds the products the search term finds.
    /// </summary>
    void Bind()
    {
        Products.DataSource = Catalog.Search(Term.Text).ToList();
        Products.DataBind();
        Log.Text = $"Bound {Products.Items.Count} products at {DateTime.Now:T}.";
    }

}
