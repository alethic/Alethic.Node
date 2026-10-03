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
/// Renders a page's components to HTML in one call to the server bundle's <c>renderOutlets</c>, on a Node engine, as
/// the page's request.
/// </summary>
/// <remarks>
/// The bundle is given each component's props as JavaScript values, every callback a function that raises its command
/// on the request and answers with a promise of the result. What the components fetch of the site is answered in
/// process, by <see cref="NodeRequest"/>.
///
/// <c>renderOutlets(requests)</c> takes <c>{ id, component, props }</c> for each component and resolves to JSON naming
/// what became of each, by id: <c>{ html }</c>, or <c>{ error: { message, stack, componentStack, dotnetErrorId } }</c>.
/// A command whose handler threw rejects with an <c>Error</c> carrying a <c>dotnetErrorId</c>, by which the exception
/// is found again to be the inner exception of the component's error.
/// </remarks>
internal static class ComponentServerRender
{

    /// <summary>
    /// Reads what <c>renderOutlets</c> answers.
    /// </summary>
    static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Renders components, awaiting the render on the request's context, which serves what the components ask of the
    /// request as they ask it: commands may be answered asynchronously.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="pool">The pool to render on.</param>
    /// <param name="bundle">The server bundle.</param>
    /// <param name="outlets">The components.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>What became of each component, by its element's id.</returns>
    /// <exception cref="TimeoutException">The render took longer than <paramref name="timeout"/>.</exception>
    public static async Task<IReadOnlyDictionary<string, ComponentRendered>> RenderAsync(NodeRequest request, NodeEnginePool pool, NodeModuleSource bundle, IReadOnlyList<ComponentOutlet> outlets, TimeSpan timeout)
    {
        var failures = new ConcurrentDictionary<string, Exception>();
        using var cancel = new CancellationTokenSource(timeout);

        try
        {
            return Read(await request.RunAsync(pool, bundle, Work(request, outlets, failures), cancel.Token), outlets, failures);
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
    /// <param name="bundle">The server bundle.</param>
    /// <param name="outlets">The components.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>What became of each component, by its element's id.</returns>
    /// <exception cref="TimeoutException">The render took longer than <paramref name="timeout"/>.</exception>
    public static IReadOnlyDictionary<string, ComponentRendered> Render(NodeRequest request, NodeEnginePool pool, NodeModuleSource bundle, IReadOnlyList<ComponentOutlet> outlets, TimeSpan timeout)
    {
        var failures = new ConcurrentDictionary<string, Exception>();
        using var cancel = new CancellationTokenSource(timeout);

        try
        {
            return Read(request.Run(pool, bundle, Work(request, outlets, failures), cancel.Token), outlets, failures);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw new TimeoutException($"The components did not render on the server within {timeout}.");
        }
    }

    /// <summary>
    /// The render, on the engine's thread: the components' requests built and handed to <c>renderOutlets</c>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="outlets">The components.</param>
    /// <param name="failures">What failed commands threw, by the id their rejections carry.</param>
    static Func<JSValue, Task<string>> Work(NodeRequest request, IReadOnlyList<ComponentOutlet> outlets, ConcurrentDictionary<string, Exception> failures)
    {
        return async exports =>
        {
            var renderOutlets = exports["renderOutlets"];
            if (renderOutlets.IsFunction() == false)
                throw new InvalidOperationException("The server bundle exports no renderOutlets function.");

            var requests = JSValue.CreateArray(outlets.Count);
            for (var i = 0; i < outlets.Count; i++)
            {
                var outlet = outlets[i];
                var item = JSValue.CreateObject();
                item["id"] = outlet.Id;
                item["component"] = outlet.Component;
                item["props"] = ToJS(outlet.Props, name => Callback(request, outlet, name, failures));
                requests[i] = item;
            }

            var rendered = await ((JSPromise)request.Call(renderOutlets, exports, requests)).AsTask();
            return (string)rendered;
        };
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
    /// Reads what <c>renderOutlets</c> answered, each error a failed command caused given what that threw.
    /// </summary>
    /// <param name="json">What it answered.</param>
    /// <param name="outlets">The components it was asked to render.</param>
    /// <param name="failures">What failed commands threw, by the id their rejections carry.</param>
    /// <exception cref="InvalidOperationException">It said nothing of a component it was asked to render.</exception>
    static IReadOnlyDictionary<string, ComponentRendered> Read(string json, IReadOnlyList<ComponentOutlet> outlets, ConcurrentDictionary<string, Exception> failures)
    {
        var read = JsonSerializer.Deserialize<Dictionary<string, ComponentRendered>>(json, ReadOptions) ?? [];
        foreach (var outlet in outlets)
            if (read.ContainsKey(outlet.Id) == false)
                throw new InvalidOperationException($"The server bundle's renderOutlets said nothing of {outlet.Component} in '{outlet.Id}'.");

        foreach (var result in read.Values)
            if (result.Error?.DotnetErrorId is string id && failures.TryGetValue(id, out var exception))
                result.Error.Exception = exception;

        return read;
    }

}
