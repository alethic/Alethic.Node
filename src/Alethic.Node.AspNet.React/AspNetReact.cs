using System;
using System.Configuration;

namespace Alethic.Node.AspNet.React;

/// <summary>
/// Where the application's React client is, and how its components render on the server: for every
/// <see cref="ReactComponent"/> in the application.
/// </summary>
/// <remarks>
/// Read from the application's <c>appSettings</c> when first wanted, and settable from code, such as in
/// <c>Application_Start</c>.
///
/// <list type="table">
/// <listheader><term>App setting</term><description>Property</description></listheader>
/// <item><term><c>Alethic:React:Script</c></term><description><see cref="Script"/></description></item>
/// <item><term><c>Alethic:React:Stylesheet</c></term><description><see cref="Stylesheet"/></description></item>
/// <item><term><c>Alethic:React:ScriptAttributes</c></term><description><see cref="ScriptAttributes"/></description></item>
/// <item><term><c>Alethic:React:ServerBundle</c></term><description><see cref="ServerBundle"/></description></item>
/// <item><term><c>Alethic:React:ServerRenderTimeout</c></term><description><see cref="ServerRenderTimeout"/>, as a <see cref="TimeSpan"/></description></item>
/// </list>
/// </remarks>
public static class AspNetReact
{

    /// <summary>
    /// The prefix of the app settings read.
    /// </summary>
    const string SettingsPrefix = "Alethic:React:";

    /// <summary>
    /// The client's browser entry: an ES module exporting <c>outlet</c> and each component a page may place, as
    /// <c>@alethic/node-aspnet-react/client</c>'s <c>createOutlets</c> makes it. A path from the application's root,
    /// <c>~/</c>, is stamped with the file's write time, so a new build is not served from a browser's cache; any other
    /// URL is used as it is.
    /// </summary>
    public static string? Script { get; set; } = Setting("Script");

    /// <summary>
    /// The client's stylesheet, linked once on each page with a component; none where <see langword="null"/>. Stamped as
    /// <see cref="Script"/> is.
    /// </summary>
    public static string? Stylesheet { get; set; } = Setting("Stylesheet");

    /// <summary>
    /// Attributes for the <c>script</c> element that places each component, as HTML: for a site whose filters rewrite
    /// inline scripts and must be told to leave this one alone.
    /// </summary>
    public static string? ScriptAttributes { get; set; } = Setting("ScriptAttributes");

    /// <summary>
    /// The client's server bundle: one self-contained CommonJS file exporting <c>renderOutlets</c>, as
    /// <c>@alethic/node-aspnet-react/server</c>'s <c>createRenderOutlets</c> makes it. Components render on the server
    /// where this is set, and only in the browser where it is not. A path from the application's root, <c>~/</c>, or an
    /// absolute one; somewhere nothing serves it, such as <c>~/App_Data</c>.
    /// </summary>
    public static string? ServerBundle { get; set; } = Setting("ServerBundle");

    /// <summary>
    /// How long a page waits for its components to render on the server before it fails. Ten seconds where not set.
    /// </summary>
    public static TimeSpan ServerRenderTimeout { get; set; } = Setting("ServerRenderTimeout") is string timeout ? TimeSpan.Parse(timeout) : TimeSpan.FromSeconds(10);

    /// <summary>
    /// The pool components render on; <see cref="AspNetNode.Pool"/> where <see langword="null"/>. Its engines must be
    /// prepared by <see cref="NodeRequest.InstallAsync"/>.
    /// </summary>
    public static NodeEnginePool? Pool { get; set; }

    /// <summary>
    /// An app setting, where it is set.
    /// </summary>
    /// <param name="name">Its name, after the prefix.</param>
    static string? Setting(string name)
    {
        var value = ConfigurationManager.AppSettings[SettingsPrefix + name];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

}
