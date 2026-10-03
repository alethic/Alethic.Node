namespace Sample.WebForms;

public partial class PartialPage
{

    /// <summary>
    /// The panel.
    /// </summary>
    protected global::System.Web.UI.UpdatePanel Panel = null!;

    /// <summary>
    /// The component inside it.
    /// </summary>
    protected global::Alethic.Node.AspNet.Components.Component Inside = null!;

    /// <summary>
    /// The button that posts it back.
    /// </summary>
    protected global::System.Web.UI.WebControls.Button Refresh = null!;

    /// <summary>
    /// When the panel rendered.
    /// </summary>
    protected global::System.Web.UI.WebControls.Literal PanelTime = null!;

    /// <summary>
    /// How many postbacks the page has seen.
    /// </summary>
    protected global::System.Web.UI.WebControls.Literal PostbackCount = null!;

    /// <summary>
    /// What the component inside reported.
    /// </summary>
    protected global::System.Web.UI.WebControls.Literal InsideLog = null!;

    /// <summary>
    /// The component outside it.
    /// </summary>
    protected global::Alethic.Node.AspNet.Components.Component Outside = null!;

}
