using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Alethic.Node.AspNet.Components;

/// <summary>
/// One component a page renders on the server: where it goes, what it is, its props, and how its commands are raised.
/// </summary>
/// <param name="id">The id of the element it renders into.</param>
/// <param name="component">The name the server bundle knows it by.</param>
/// <param name="props">Its props.</param>
/// <param name="raise">Raises one of its commands, on the request: the command's name and what the component called the
/// callback with. Completes with the command's result as JSON, or <see langword="null"/> for none.</param>
internal sealed class ComponentOutlet(string id, string component, ComponentObject props, Func<string, IReadOnlyList<JsonElement>, Task<string?>> raise)
{

    /// <summary>
    /// The id of the element it renders into.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// The name the server bundle knows it by.
    /// </summary>
    public string Component { get; } = component;

    /// <summary>
    /// Its props.
    /// </summary>
    public ComponentObject Props { get; } = props;

    /// <summary>
    /// Raises one of its commands, on the request.
    /// </summary>
    public Func<string, IReadOnlyList<JsonElement>, Task<string?>> Raise { get; } = raise;

}
