namespace Sample.WebForms;

public partial class DemoPage
{

    /// <summary>
    /// The panel the search posts back.
    /// </summary>
    protected global::System.Web.UI.UpdatePanel Results = null!;

    /// <summary>
    /// The search term.
    /// </summary>
    protected global::System.Web.UI.WebControls.TextBox Term = null!;

    /// <summary>
    /// The search button.
    /// </summary>
    protected global::System.Web.UI.WebControls.Button Search = null!;

    /// <summary>
    /// The products.
    /// </summary>
    protected global::System.Web.UI.WebControls.Repeater Products = null!;

    /// <summary>
    /// What happened.
    /// </summary>
    protected global::System.Web.UI.WebControls.Literal Log = null!;

}
