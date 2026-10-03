using System;
using System.Web.UI;

namespace Sample.WebForms;

/// <summary>
/// The layout every page shares, with the cart's badge in its header.
/// </summary>
public partial class Site : MasterPage
{

    /// <summary>
    /// Gives the badge the cart's count, from code, on every request.
    /// </summary>
    /// <param name="sender">The master page.</param>
    /// <param name="e">The event's arguments.</param>
    protected void Page_Load(object sender, EventArgs e)
    {
        Badge.Props["count"] = Cart.Count(Context);
    }

}
