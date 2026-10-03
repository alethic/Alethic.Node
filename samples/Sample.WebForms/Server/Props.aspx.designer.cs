namespace Sample.WebForms;

public partial class PropsPage
{

    /// <summary>
    /// Props declared in markup.
    /// </summary>
    protected global::Alethic.Node.AspNet.Components.Component Declared = null!;

    /// <summary>
    /// Props bound.
    /// </summary>
    protected global::Alethic.Node.AspNet.Components.Component Bound = null!;

    /// <summary>
    /// Props changed from code.
    /// </summary>
    protected global::Alethic.Node.AspNet.Components.Component FromCode = null!;

    /// <summary>
    /// The button that changes them.
    /// </summary>
    protected global::System.Web.UI.WebControls.Button Click = null!;

    /// <summary>
    /// What the page heard.
    /// </summary>
    protected global::System.Web.UI.WebControls.Literal Log = null!;

}
