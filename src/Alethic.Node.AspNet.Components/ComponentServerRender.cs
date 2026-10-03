using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.JavaScript.NodeApi;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// Renders a page's components to HTML with a server module's <c>render</c>, on a Node engine, as the page's request.
/// </summary>
/// <remarks>
/// Each component is found in the module by its name, an export or a dotted path through one; a name the module has
/// nothing at fails that component. The module's <c>render(component, props, page)</c> renders one component to HTML,
/// or throws: it is all a module provides. The rest is the library's, in <see cref="PageScript"/>, which the engine is
/// given once: it renders a page's components one after another, all with the same <c>page</c> object, which they
/// share; it makes every callback in the props a function that raises its command on the request, counts the commands
/// a component has called, waits for their answers, and fails a component that left one's rejection unhandled; and it
/// reports what became of each. What the components fetch of the site is answered in process, by
/// <see cref="NodeRequest"/>.
/// </remarks>
internal static class ComponentServerRender
{

    /// <summary>
    /// The name of the global the engine's half of this lives under.
    /// </summary>
    const string BridgeName = "__alethicNodeComponents";

    /// <summary>
    /// Renders a page: each component with the module's <c>render</c>, its callbacks tracked, its failures reported.
    /// Completes with the bridge, whose <c>renderPage(render, requests)</c> resolves to the JSON of what became of each
    /// component, by id: <c>{ html }</c>, or <c>{ error: { message, stack, componentStack, dotnetErrorId } }</c>.
    /// </summary>
    /// <remarks>
    /// A command's rejection is marked as the component's by the error itself, since that is all an unhandled rejection
    /// reports, and a component that chains on a callback's promise leaves its own promise unhandled, not the
    /// callback's. Node reports a rejection left unhandled once the microtasks it was rejected in have run: a turn of
    /// the event loop after a component's commands are all answered.
    /// </remarks>
    const string PageScript = """
        (() => {
            const owners = new WeakMap();

            process.on('unhandledRejection', reason => {
                const owner = reason !== null && typeof reason === 'object' ? owners.get(reason) : undefined;
                if (owner !== undefined && owner.page.unhandled.has(owner.id) === false) {
                    owner.page.unhandled.set(owner.id, reason);
                }
            });

            const turn = () => new Promise(resolve => setImmediate(resolve));

            function track(value, page, id) {
                if (typeof value === 'function') {
                    const callback = value;
                    const tracked = (...args) => {
                        page.pending++;
                        let called;
                        try {
                            called = Promise.resolve(callback(...args));
                        } catch (e) {
                            called = Promise.reject(e);
                        }

                        return called
                            .then(v => v, e => {
                                if (e !== null && typeof e === 'object') {
                                    owners.set(e, { page, id });
                                }

                                throw e;
                            })
                            .finally(() => {
                                page.pending--;
                            });
                    };

                    Object.defineProperty(tracked, 'name', { value: callback.name });
                    return tracked;
                }

                if (Array.isArray(value)) {
                    return value.map(i => track(i, page, id));
                }

                if (value !== null && typeof value === 'object') {
                    return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, track(v, page, id)]));
                }

                return value;
            }

            function describe(e) {
                const error = e !== null && typeof e === 'object' ? e : {};
                return {
                    message: e instanceof Error ? e.message : String(e),
                    stack: typeof error.stack === 'string' ? error.stack : undefined,
                    componentStack: typeof error.componentStack === 'string' ? error.componentStack : undefined,
                    dotnetErrorId: typeof error.dotnetErrorId === 'string' ? error.dotnetErrorId : undefined,
                };
            }

            async function renderPage(render, requests) {
                const shared = {};
                const page = { pending: 0, unhandled: new Map() };
                const rendered = {};

                for (const { id, component, props } of requests) {
                    let html;
                    let failure;
                    try {
                        html = await render(component, track(props, page, id), shared);
                        if (typeof html !== 'string') {
                            throw new TypeError(`The server module's render resolved to ${typeof html}, not a string of HTML.`);
                        }
                    } catch (e) {
                        failure = e;
                    }

                    while (page.pending > 0) {
                        await turn();
                    }

                    await turn();
                    failure ??= page.unhandled.get(id);
                    rendered[id] = failure !== undefined ? { error: describe(failure) } : { html };
                }

                return JSON.stringify(rendered);
            }

            return { renderPage };
        })();
        """;

    /// <summary>
    /// Reads what <c>renderPage</c> answers.
    /// </summary>
    static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Renders components, awaiting the render on the request's context, which serves what the components ask of the
    /// request as they ask it: commands may be answered asynchronously.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="pool">The pool to render on.</param>
    /// <param name="module">The server module.</param>
    /// <param name="outlets">The components.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>What became of each component, by its element's id.</returns>
    /// <exception cref="TimeoutException">The render took longer than <paramref name="timeout"/>.</exception>
    public static async Task<IReadOnlyDictionary<string, ComponentRendered>> RenderAsync(NodeRequest request, NodeEnginePool pool, NodeModuleSource module, IReadOnlyList<ComponentOutlet> outlets, TimeSpan timeout)
    {
        var failures = new ConcurrentDictionary<string, Exception>();
        var missing = new ConcurrentDictionary<string, string>();
        using var cancel = new CancellationTokenSource(timeout);

        try
        {
            return Read(await request.RunAsync(pool, module, Work(request, outlets, failures, missing), cancel.Token), outlets, failures, missing);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw new TimeoutException($"The components did not render on the server within {timeout}.");
        }
    }

    /// <summary>
    /// Renders components, blocking the request's thread, which serves what the components ask of the request as they
    /// ask it: each command must be answered before its handler returns.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="pool">The pool to render on.</param>
    /// <param name="module">The server module.</param>
    /// <param name="outlets">The components.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>What became of each component, by its element's id.</returns>
    /// <exception cref="TimeoutException">The render took longer than <paramref name="timeout"/>.</exception>
    public static IReadOnlyDictionary<string, ComponentRendered> Render(NodeRequest request, NodeEnginePool pool, NodeModuleSource module, IReadOnlyList<ComponentOutlet> outlets, TimeSpan timeout)
    {
        var failures = new ConcurrentDictionary<string, Exception>();
        var missing = new ConcurrentDictionary<string, string>();
        using var cancel = new CancellationTokenSource(timeout);

        try
        {
            return Read(request.Run(pool, module, Work(request, outlets, failures, missing), cancel.Token), outlets, failures, missing);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw new TimeoutException($"The components did not render on the server within {timeout}.");
        }
    }

    /// <summary>
    /// The render, on the engine's thread: each component found in the module, and the page rendered with the module's
    /// <c>render</c>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="outlets">The components.</param>
    /// <param name="failures">What failed commands threw, by the id their rejections carry.</param>
    /// <param name="missing">Why components were not found, by their element's id.</param>
    static Func<JSValue, Task<string>> Work(NodeRequest request, IReadOnlyList<ComponentOutlet> outlets, ConcurrentDictionary<string, Exception> failures, ConcurrentDictionary<string, string> missing)
    {
        return async exports =>
        {
            var render = exports["render"];
            if (render.IsFunction() == false)
                throw new InvalidOperationException("The server module exports no render function.");

            var requests = JSValue.CreateArray(0);
            var count = 0;
            foreach (var outlet in outlets)
            {
                if (Find(exports, outlet.Component) is not JSValue component)
                {
                    missing[outlet.Id] = $"The server module has no {outlet.Component}.";
                    continue;
                }

                var item = JSValue.CreateObject();
                item["id"] = outlet.Id;
                item["component"] = component;
                item["props"] = ToJS(outlet.Props, name => Callback(request, outlet, name, failures));
                requests[count++] = item;
            }

            var bridge = Bridge();
            var rendered = await ((JSPromise)request.Call(bridge["renderPage"], bridge, render, requests)).AsTask();
            return (string)rendered;
        };
    }

    /// <summary>
    /// The engine's half of this, made the first time a page renders on the engine. On the engine's thread.
    /// </summary>
    static JSValue Bridge()
    {
        var global = JSValue.Global;
        var bridge = global[BridgeName];
        if (bridge.IsObject())
            return bridge;

        bridge = JSValue.RunScript(PageScript);
        global[BridgeName] = bridge;
        return bridge;
    }

    /// <summary>
    /// What a name is in a module's exports: an export, or a dotted path through one; <see langword="null"/> where it is
    /// nothing. On the engine's thread.
    /// </summary>
    /// <param name="exports">The module's exports.</param>
    /// <param name="name">The name.</param>
    static JSValue? Find(JSValue exports, string name)
    {
        var at = exports;
        foreach (var key in name.Split('.'))
        {
            if (at.IsObject() == false && at.IsFunction() == false)
                return null;

            at = at[key];
        }

        return at.IsNullOrUndefined() ? null : (JSValue?)at;
    }

    /// <summary>
    /// Props as JavaScript values, made on the engine's thread, each callback a function made by
    /// <paramref name="callback"/>.
    /// </summary>
    /// <param name="value">The props, or a value in them.</param>
    /// <param name="callback">Makes the function a command is, from its name.</param>
    internal static JSValue ToJS(ComponentValue value, Func<string, JSValue> callback)
    {
        switch (value)
        {
            case ComponentObject obj:
                var o = JSValue.CreateObject();
                foreach (var pair in obj)
                    o[pair.Key] = ToJS(pair.Value, callback);
                return o;

            case ComponentArray array:
                var a = JSValue.CreateArray(array.Count);
                for (var i = 0; i < array.Count; i++)
                    a[i] = ToJS(array[i]!, callback);
                return a;

            case ComponentCommand command:
                return callback(command.CommandName);

            default:
                // A scalar: what JSON makes of it is what JavaScript does.
                return JSValue.Global["JSON"].CallMethod("parse", value.ToJson());
        }
    }

    /// <summary>
    /// The function a command is during a server render: it hands the command to the request, which raises it there,
    /// and answers with a promise of the command's result. The component goes on rendering meanwhile, as it would in
    /// the browser while the command posted back.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="outlet">The component whose props it is in.</param>
    /// <param name="name">The command's name.</param>
    /// <param name="failures">What failed commands threw, by the id their rejections carry.</param>
    static JSValue Callback(NodeRequest request, ComponentOutlet outlet, string name, ConcurrentDictionary<string, Exception> failures)
    {
        return JSValue.CreateFunction(name, args =>
        {
            // Read here, where the values are legal: the arguments as JSON, which any thread can carry.
            var values = JSValue.CreateArray(args.Length);
            for (var i = 0; i < args.Length; i++)
                values[i] = args[i];

            var json = (string)JSValue.Global["JSON"].CallMethod("stringify", values);
            var promise = JSValue.CreatePromise(out var deferred);
            var done = request.InvokeAsync(() => outlet.Raise(name, Arguments(json)));

            _ = SettleAsync(deferred, done, failures);
            return promise;
        });
    }

    /// <summary>
    /// A callback's arguments, as JSON elements that outlive their document.
    /// </summary>
    /// <param name="json">The arguments, as a JSON array.</param>
    static IReadOnlyList<JsonElement> Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(i => i.Clone()).ToList();
    }

    /// <summary>
    /// Settles a command's promise with its result, or rejects it with what its handler threw, marked with an id by
    /// which the exception is found again. Started on the engine's thread, so it resumes there.
    /// </summary>
    /// <param name="deferred">The promise's settling side.</param>
    /// <param name="done">The command.</param>
    /// <param name="failures">What failed commands threw, by the id their rejections carry.</param>
    static async Task SettleAsync(JSPromise.Deferred deferred, Task<string?> done, ConcurrentDictionary<string, Exception> failures)
    {
        try
        {
            var json = await done;
            deferred.Resolve(json is null ? JSValue.Undefined : JSValue.Global["JSON"].CallMethod("parse", json));
        }
        catch (Exception e)
        {
            var id = Guid.NewGuid().ToString("N");
            failures[id] = e;

            var error = JSValue.Global["Error"].CallAsConstructor(e.Message);
            error["stack"] = e.ToString();
            error["dotnetErrorId"] = id;
            deferred.Reject(new JSError(error));
        }
    }

    /// <summary>
    /// Reads what <c>renderPage</c> answered, each error a failed command caused given what that threw.
    /// </summary>
    /// <param name="json">What it answered.</param>
    /// <param name="outlets">The components it was asked to render.</param>
    /// <param name="failures">What failed commands threw, by the id their rejections carry.</param>
    /// <param name="missing">Why components were not found, by their element's id.</param>
    /// <exception cref="InvalidOperationException">It said nothing of a component it was asked to render.</exception>
    static IReadOnlyDictionary<string, ComponentRendered> Read(string json, IReadOnlyList<ComponentOutlet> outlets, ConcurrentDictionary<string, Exception> failures, ConcurrentDictionary<string, string> missing)
    {
        var read = JsonSerializer.Deserialize<Dictionary<string, ComponentRendered>>(json, ReadOptions) ?? [];
        foreach (var pair in missing)
            read[pair.Key] = new ComponentRendered() { Error = new ComponentRenderError() { Message = pair.Value } };

        foreach (var outlet in outlets)
            if (read.ContainsKey(outlet.Id) == false)
                throw new InvalidOperationException($"The server render said nothing of {outlet.Component} in '{outlet.Id}'.");

        foreach (var result in read.Values)
            if (result.Error?.DotnetErrorId is string id && failures.TryGetValue(id, out var exception))
                result.Error.Exception = exception;

        return read;
    }

}
