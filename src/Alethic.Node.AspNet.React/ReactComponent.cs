using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace Alethic.Node.AspNet.React;

/// <summary>
/// Hosts a React component on a Web Forms page.
/// </summary>
/// <remarks>
/// Renders an element, and a script that imports the application's React client, <see cref="AspNetReact.Script"/>, and
/// places the component in the element with the client's <c>outlet</c> function. The client keeps one React tree for the
/// whole page and renders each component placed this way into its element through a portal, so every component on the
/// page shares the contexts its root establishes. A component can be hosted once the client exports it.
///
/// The component's props are <see cref="Props"/>: declared in markup with nested <see cref="ReactProp"/>s, which build it
/// on <c>Init</c>, as an <c>asp:ListItem</c> builds a list's items, and changed from code from there on. Props are data,
/// kept in view state where code changed them, and callbacks: a <see cref="ReactCallback"/> in markup, or a
/// <see cref="ReactCommand"/> from code, which the component receives as a function raising a command by name, as a
/// button's <c>CommandName</c> raises its container's command. The browser dispatches on the name in
/// <see cref="OnClientCommand"/>, whose returning <see langword="false"/> cancels the rest, and the page in its handler of
/// <see cref="Command"/>, to which the command posts back; inside an <see cref="UpdatePanel"/> the postback is a partial
/// one, as for any control in it.
///
/// On a page with a <see cref="ScriptManager"/> the script is registered with it rather than written after the element,
/// so that a control inside an <see cref="UpdatePanel"/> places its component again after a partial postback: the panel's
/// new markup arrives without running the scripts in it, but registered scripts run. That is also why the script is a
/// classic one that imports the client with <c>import()</c>, not a module.
///
/// Where the application has a <see cref="AspNetReact.ServerBundle"/>, the page's components are also rendered to HTML on
/// the server, on Node embedded in the worker process, all in one call once the page's <c>PreRender</c> is complete, and
/// each control sends its component's HTML inside its element. The browser shows that HTML until the component has
/// rendered there, then replaces it. A command the component raises while it renders on the server raises
/// <see cref="Command"/> there and then, in the page's own request. On a page that is <c>Async="true"</c> the render is an
/// async page task, and a command may be answered asynchronously; on any other the request blocks while it renders, and a
/// command answered asynchronously fails.
///
/// Nothing that fails is passed over. What the component throws while it renders on the server, or a promise of one of
/// its callbacks it rejects without catching, is a <see cref="ReactRenderException"/> thrown from this control's render;
/// what stops the server render as a whole, or a client that is not built, fails the page. Either reaches the page's
/// error handling as any control's or page's failure does.
/// </remarks>
[ParseChildren(false)]
[PersistChildren(true)]
[ControlBuilder(typeof(ReactPropsBuilder))]
public class ReactComponent : WebControl, IPostBackEventHandler
{

    /// <summary>
    /// Marks a request whose page has already been given the stylesheet, so a second control does not write it again.
    /// </summary>
    static readonly object StylesheetRenderedKey = new();

    /// <summary>
    /// The controls on the page that render on the server, collected for the one call that renders them.
    /// </summary>
    static readonly object ServerRenderedKey = new();

    /// <summary>
    /// The props as the markup declared them, against which <see cref="SaveViewState"/> tells whether code changed them.
    /// </summary>
    string? _declaredProps;

    /// <summary>
    /// The script that places the component, once <see cref="OnPreRender"/> has made it, where no
    /// <see cref="ScriptManager"/> took it.
    /// </summary>
    string? _outletScript;

    /// <summary>
    /// The component's HTML, rendered on the server, where it was.
    /// </summary>
    string? _serverHtml;

    /// <summary>
    /// What the component threw while it rendered on the server, which the control's render throws.
    /// </summary>
    ReactRenderError? _serverError;

    /// <summary>
    /// Initializes a new instance, rendering a <c>div</c>.
    /// </summary>
    public ReactComponent()
        : base(HtmlTextWriterTag.Div)
    {

    }

    /// <summary>
    /// The component to place, by the name the client exports it under.
    /// </summary>
    public string? Component { get; set; }

    /// <summary>
    /// The component's props: built from the markup's <see cref="ReactProp"/>s on <c>Init</c>, and changed from code from
    /// there on — <c>Props["title"] = "Pipettes"</c>, <c>Props["onSelect"] = new ReactCommand("Select")</c>. Kept in view
    /// state where code changed them, on the same terms as any other property of a control.
    /// </summary>
    public ReactObject Props { get; private set; } = new();

    /// <summary>
    /// JavaScript that evaluates to the function the browser calls with each command the component raises: its name and
    /// its arguments. Returning <see langword="false"/> from it keeps the command from posting back, as from a button's
    /// <c>OnClientClick</c>; otherwise every command posts back. Evaluated each time a command is raised, so the function
    /// may be defined anywhere on the page.
    /// </summary>
    public string? OnClientCommand { get; set; }

    /// <summary>
    /// Whether the component renders on the server, where the application has a <see cref="AspNetReact.ServerBundle"/>.
    /// <see langword="true"/> unless set.
    /// </summary>
    public bool ServerRender { get; set; } = true;

    /// <summary>
    /// Raised for each command the component raises: posted back from the browser, or, during a server render, there and
    /// then. Dispatch on <see cref="CommandEventArgs.CommandName"/>. The command then bubbles, as a button's does, so a
    /// container such as a <c>Repeater</c> raises it as its item command too.
    /// </summary>
    public event EventHandler<ReactCommandEventArgs>? Command;

    /// <summary>
    /// Builds <see cref="Props"/> from the markup.
    /// </summary>
    /// <param name="e">The event's arguments.</param>
    protected override void OnInit(EventArgs e)
    {
        base.OnInit(e);
        DeclareProps();
    }

    /// <summary>
    /// Binds the control and its props, then builds <see cref="Props"/> from the markup again, since a bound value is
    /// only there from now. Rebinding resets what code changed, as it does a bound control's values.
    /// </summary>
    public override void DataBind()
    {
        base.DataBind();
        DeclareProps();
    }

    /// <summary>
    /// Builds <see cref="Props"/> from the props the markup declares, and remembers them as declared.
    /// </summary>
    void DeclareProps()
    {
        Props = ReactProp.Build(new ReactObject(), this);
        _declaredProps = Props.ToJson();
    }

    /// <summary>
    /// Restores the control's view state, and <see cref="Props"/> with it where code had changed them.
    /// </summary>
    /// <param name="savedState">What <see cref="SaveViewState"/> saved.</param>
    protected override void LoadViewState(object? savedState)
    {
        var state = (Pair?)savedState;
        base.LoadViewState(state?.First);

        if (state?.Second is string props)
            Props = (ReactObject)ReactValue.Parse(props);
    }

    /// <summary>
    /// Saves the control's view state, and <see cref="Props"/> with it as JSON where code changed them from what the
    /// markup declared.
    /// </summary>
    /// <returns>The base state and the props, or <see langword="null"/> where there is neither.</returns>
    protected override object? SaveViewState()
    {
        var state = base.SaveViewState();
        var props = Props.ToJson();
        if (props == _declaredProps)
            props = null;

        return state is null && props is null ? null : new Pair(state, props);
    }

    /// <summary>
    /// Adds the element's attributes, always including its id.
    /// </summary>
    /// <param name="writer">The writer the element is rendered to.</param>
    protected override void AddAttributesToRender(HtmlTextWriter writer)
    {
        // The script finds the element by its id, which WebControl writes only when the control was given one.
        if (ID is null)
            writer.AddAttribute(HtmlTextWriterAttribute.Id, ClientID);

        base.AddAttributesToRender(writer);
    }

    /// <summary>
    /// Makes the script that places the component, and registers it with the page's <see cref="ScriptManager"/> where
    /// there is one; enlists the control in the page's server render where there is one.
    /// </summary>
    /// <param name="e">The event's arguments.</param>
    /// <exception cref="InvalidOperationException">There is no <see cref="Component"/>, or no client.</exception>
    protected override void OnPreRender(EventArgs e)
    {
        base.OnPreRender(e);

        if (string.IsNullOrEmpty(Component))
            throw new InvalidOperationException($"ReactComponent '{ID}' needs a Component, the name of an export of the React client.");

        var script = Url(AspNetReact.Script ?? throw new InvalidOperationException("No React client is configured: set Alethic:React:Script, or AspNetReact.Script."))
            ?? throw new InvalidOperationException($"The React client is not built: there is no {AspNetReact.Script}.");

        // The function each callback raises its command through, in the browser. It answers with a promise of the
        // command's result: resolved when the partial postback the command made comes back, with the result the server
        // sent with it, or rejected with its error. A full postback replaces the page, and the promise with it.
        var dispatch = new StringBuilder("function (name, args) {");
        if (string.IsNullOrEmpty(OnClientCommand) == false)
            dispatch.Append(" if ((").Append(OnClientCommand).Append(")(name, args) === false) return Promise.resolve();");

        dispatch.Append(" return new Promise(function (resolve, reject) {");
        dispatch.Append(" var prm = window.Sys && Sys.WebForms && Sys.WebForms.PageRequestManager && Sys.WebForms.PageRequestManager.getInstance();");
        dispatch.Append(" if (prm) { var done = function (sender, e) { prm.remove_endRequest(done); var error = e.get_error(); if (error) { e.set_errorHandled(true); reject(error); return; }");
        dispatch.Append(" var item = e.get_dataItems()[").Append(ToScript(ClientID)).Append("]; resolve(item === undefined ? undefined : JSON.parse(item)); }; prm.add_endRequest(done); }");

        // Posts back, as a button does unless its OnClientClick returned false; off the component's call stack, as the
        // stock controls' AutoPostBack does: the PageRequestManager's __doPostBack reads its callers' arguments, which a
        // strict-mode caller, such as a bundled client, refuses.
        dispatch.Append(" var posted = JSON.stringify({ name: name, args: args }); setTimeout(function () { __doPostBack(").Append(ToScript(UniqueID)).Append(", posted); }, 0);");
        dispatch.Append(" }); }");
        Page.ClientScript.GetPostBackEventReference(this, "");

        var props = new StringBuilder();
        WriteScript(props, Props);
        var attributes = string.IsNullOrWhiteSpace(AspNetReact.ScriptAttributes) ? "" : " " + AspNetReact.ScriptAttributes!.Trim();
        _outletScript = string.Format(
            "<script{0}>import({1}).then(function (m) {{ var d = {2}; var c = function (n) {{ return function () {{ return d(n, Array.prototype.slice.call(arguments)); }}; }}; m.outlet(m[{3}], document.getElementById({4}), {5}); }});</script>",
            attributes,
            ToScript(script),
            dispatch,
            ToScript(Component),
            ToScript(ClientID),
            props);

        if (ScriptManager.GetCurrent(Page) is ScriptManager scriptManager)
        {
            ScriptManager.RegisterStartupScript(this, typeof(ReactComponent), ClientID, _outletScript, false);
            _outletScript = null;

            // Its commands post back partially wherever it is, as an AJAX control's do: a full postback would replace the
            // component that asked before its answer came back.
            scriptManager.RegisterAsyncPostBackControl(this);
        }

        if (ServerRender && AspNetReact.ServerBundle is not null)
            Enlist();
    }

    /// <summary>
    /// Adds the control to the page's server render, starting one where this is the first.
    /// </summary>
    void Enlist()
    {
        if (Context.Items[ServerRenderedKey] is List<ReactComponent> controls)
        {
            controls.Add(this);
            return;
        }

        controls = [this];
        Context.Items[ServerRenderedKey] = controls;

        // On an asynchronous page, an async page task, which the page awaits after PreRender, so that a command may be
        // answered asynchronously. On any other, once PreRender is complete, the request blocking.
        var page = Page;
        var context = Context;
        if (page.IsAsync)
            page.RegisterAsyncTask(new PageAsyncTask(() => RenderOnServerAsync(page, context, controls)));
        else
            page.PreRenderComplete += (sender, args) => RenderOnServer(page, context, controls);
    }

    /// <summary>
    /// Writes props as a JavaScript literal for the outlet script, each callback a call to its <c>c</c>, which makes the
    /// function raising the command.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="value">The props, or a value in them.</param>
    static void WriteScript(StringBuilder script, ReactValue value)
    {
        switch (value)
        {
            case ReactObject obj:
                script.Append('{');
                var first = true;
                foreach (var pair in obj)
                {
                    if (first == false)
                        script.Append(',');

                    script.Append(ToScript(pair.Key)).Append(':');
                    WriteScript(script, pair.Value);
                    first = false;
                }

                script.Append('}');
                break;

            case ReactArray array:
                script.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                        script.Append(',');

                    WriteScript(script, array[i]!);
                }

                script.Append(']');
                break;

            case ReactCommand command:
                script.Append("c(").Append(ToScript(command.CommandName)).Append(')');
                break;

            default:
                // A scalar's JSON, which the writer's default encoder keeps from closing the script element.
                script.Append(value.ToJson());
                break;
        }
    }

    /// <summary>
    /// Renders every component on the page that is to render on the server, in one call, now that the page has settled
    /// what each is given.
    /// </summary>
    /// <remarks>
    /// What a component throws comes back as its control's error, thrown when the control renders. What stops the render
    /// as a whole — Node not available here, the render timing out — is thrown from here, and fails the page, as any
    /// page's failure to render does.
    /// </remarks>
    /// <param name="page">The page.</param>
    /// <param name="context">The page's request.</param>
    /// <param name="controls">The controls.</param>
    static async Task RenderOnServerAsync(Page page, HttpContext context, List<ReactComponent> controls)
    {
        Rendered(controls, await ReactServerRender.RenderAsync(
            new NodeRequest(context),
            AspNetReact.Pool ?? AspNetNode.Pool,
            Bundle(page),
            controls.Select(i => i.Outlet(true)).ToList(),
            AspNetReact.ServerRenderTimeout));
    }

    /// <summary>
    /// Renders every component on the page that is to render on the server, as <see cref="RenderOnServerAsync"/> does,
    /// for a page that is not asynchronous: a command may only be answered synchronously.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="context">The page's request.</param>
    /// <param name="controls">The controls.</param>
    static void RenderOnServer(Page page, HttpContext context, List<ReactComponent> controls)
    {
        Rendered(controls, ReactServerRender.Render(
            new NodeRequest(context),
            AspNetReact.Pool ?? AspNetNode.Pool,
            Bundle(page),
            controls.Select(i => i.Outlet(false)).ToList(),
            AspNetReact.ServerRenderTimeout));
    }

    /// <summary>
    /// The server bundle.
    /// </summary>
    /// <param name="page">The page, to map its path.</param>
    static NodeModuleSource Bundle(Page page)
    {
        var path = AspNetReact.ServerBundle!;
        return NodeModuleSource.FromFile(path.StartsWith("~/", StringComparison.Ordinal) ? page.Server.MapPath(path) : path);
    }

    /// <summary>
    /// This control, as the server render takes it.
    /// </summary>
    /// <param name="isAsync">Whether the page is asynchronous, so that a command may be answered asynchronously.</param>
    internal ReactOutlet Outlet(bool isAsync)
    {
        return new ReactOutlet(ClientID, Component!, Props, (name, args) =>
        {
            var e = RaiseCommand(name, args, true);
            if (isAsync == false && e.Result is Task task && task.IsCompleted == false)
                throw new InvalidOperationException($"ReactComponent '{ID}': the command '{name}' answers asynchronously, which needs its page to be Async=\"true\".");

            return ResultJsonAsync(e.Result);
        });
    }

    /// <summary>
    /// Gives each control what became of its component.
    /// </summary>
    /// <param name="controls">The controls.</param>
    /// <param name="rendered">What became of each component, by its element's id.</param>
    static void Rendered(List<ReactComponent> controls, IReadOnlyDictionary<string, ReactRendered> rendered)
    {
        foreach (var control in controls)
        {
            var result = rendered[control.ClientID];
            control._serverHtml = result.Html;
            control._serverError = result.Error;
        }
    }

    /// <summary>
    /// Raises the command the browser posted back.
    /// </summary>
    /// <remarks>
    /// Not validated against the arguments the page rendered, as the stock controls' postbacks are: the arguments are
    /// whatever the component called its callback with. The name is: only a command the props hold can be raised.
    /// </remarks>
    /// <param name="eventArgument">The command's name and arguments, as JSON.</param>
    /// <exception cref="InvalidOperationException">The command answers asynchronously on a page that is not.</exception>
    public void RaisePostBackEvent(string eventArgument)
    {
        var posted = JsonSerializer.Deserialize<PostedCommand>(eventArgument, ReactValue.WebOptions)
            ?? throw new InvalidOperationException($"ReactComponent '{ID}' was posted no command.");
        var e = RaiseCommand(posted.Name ?? "", posted.Args, false);

        // The result goes back with a partial postback, to the promise the component's callback answered with. A full
        // postback replaces the page, and nobody is left to hear it.
        var scriptManager = ScriptManager.GetCurrent(Page);
        if (e.Result is null || scriptManager is null || scriptManager.IsInAsyncPostBack == false)
            return;

        if (e.Result is Task task && task.IsCompleted == false)
        {
            if (Page.IsAsync == false)
                throw new InvalidOperationException($"ReactComponent '{ID}': the command '{e.CommandName}' answers asynchronously, which needs its page to be Async=\"true\".");

            Page.RegisterAsyncTask(new PageAsyncTask(async () => scriptManager.RegisterDataItem(this, await ResultJsonAsync(e.Result) ?? "null")));
            return;
        }

        scriptManager.RegisterDataItem(this, ResultJsonAsync(e.Result).GetAwaiter().GetResult() ?? "null");
    }

    /// <summary>
    /// Raises a command the component raised, where the props hold it: the control's <see cref="Command"/>, then up
    /// through its containers, as a button's command bubbles.
    /// </summary>
    /// <param name="name">The command's name.</param>
    /// <param name="args">What the component called the callback with.</param>
    /// <param name="isServerRender">Whether it was raised while the component rendered on the server.</param>
    /// <exception cref="InvalidOperationException">The props hold no such command.</exception>
    internal ReactCommandEventArgs RaiseCommand(string name, IReadOnlyList<JsonElement>? args, bool isServerRender)
    {
        if (Commands(Props).Contains(name) == false)
            throw new InvalidOperationException($"ReactComponent '{ID}' has no command '{name}'.");

        var e = new ReactCommandEventArgs(name, args, isServerRender);
        Command?.Invoke(this, e);
        RaiseBubbleEvent(this, e);
        return e;
    }

    /// <summary>
    /// A command's result as JSON: awaited, where it is a task, and read as props are.
    /// </summary>
    /// <param name="result">The result, or a task of it.</param>
    /// <returns>The result as JSON, or <see langword="null"/> for none.</returns>
    static async Task<string?> ResultJsonAsync(object? result)
    {
        if (result is Task task)
        {
            await task;

            // Task<T>'s result, where it has one; a plain Task has none.
            var type = task.GetType();
            result = type.IsGenericType && type.GetGenericArguments()[0].Name != "VoidTaskResult"
                ? type.GetProperty(nameof(Task<object>.Result))!.GetValue(task)
                : null;
        }

        return result is null ? null : ReactValue.FromObject(result).ToJson();
    }

    /// <summary>
    /// The names of the commands in props.
    /// </summary>
    /// <param name="value">The props, or a value in them.</param>
    static IEnumerable<string> Commands(ReactValue value)
    {
        return value switch
        {
            ReactCommand command => [command.CommandName],
            ReactObject obj => obj.SelectMany(i => Commands(i.Value)),
            ReactArray array => array.SelectMany(Commands),
            _ => [],
        };
    }

    /// <summary>
    /// Renders the stylesheet, once per page, the element, and, where no <see cref="ScriptManager"/> took it, the script
    /// that places the component.
    /// </summary>
    /// <param name="writer">The writer to render to.</param>
    /// <exception cref="ReactRenderException">The component failed to render on the server.</exception>
    protected override void Render(HtmlTextWriter writer)
    {
        if (Context.Items.Contains(StylesheetRenderedKey) == false)
        {
            Context.Items[StylesheetRenderedKey] = true;
            if (AspNetReact.Stylesheet is string stylesheet && Url(stylesheet) is string href)
                writer.Write("<link rel=\"stylesheet\" href=\"" + HttpUtility.HtmlAttributeEncode(href) + "\" />");
        }

        // What the component threw on the server, thrown here, from the control's own render, as any control's failure to
        // render is.
        if (_serverError is not null)
            throw new ReactRenderException(Component!, ID, _serverError.Message, _serverError.Stack, _serverError.ComponentStack, _serverError.Exception);

        base.Render(writer);

        if (_outletScript is not null)
            writer.Write(_outletScript);
    }

    /// <summary>
    /// Writes the component's server-rendered HTML inside the element, where there is any; never the props declared in
    /// markup, which are the component's and not the page's.
    /// </summary>
    /// <param name="writer">The writer to render to.</param>
    protected override void RenderContents(HtmlTextWriter writer)
    {
        if (_serverHtml is not null)
            writer.Write(_serverHtml);
    }

    /// <summary>
    /// Writes a value as a JavaScript literal. The default encoder escapes <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c>, so
    /// no value can close the script element.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    static string ToScript<T>(T value)
    {
        return JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// A client file's URL: a path from the application's root stamped with the file's write time, so a new build is not
    /// served from a browser's cache, and <see langword="null"/> where the file is not there; any other URL as it is.
    /// </summary>
    /// <param name="path">The path or URL.</param>
    string? Url(string path)
    {
        if (path.StartsWith("~/", StringComparison.Ordinal) == false)
            return path;

        var file = Context.Server.MapPath(path);
        if (File.Exists(file) == false)
            return null;

        return ResolveUrl(path) + "?v=" + File.GetLastWriteTimeUtc(file).Ticks;
    }

    /// <summary>
    /// What a command's callback posts back.
    /// </summary>
    sealed class PostedCommand
    {

        /// <summary>
        /// The command's name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// What the component called the callback with.
        /// </summary>
        public JsonElement[]? Args { get; set; }

    }

}
