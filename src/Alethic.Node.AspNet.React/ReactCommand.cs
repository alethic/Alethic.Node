using System;
using System.Text.Json;

namespace Alethic.Node.AspNet.React;

/// <summary>
/// A callback in a <see cref="ReactComponent"/>'s props: a function the component calls, which raises the control's
/// command by name, as a button's <c>CommandName</c> raises its container's command.
/// </summary>
/// <remarks>
/// All a callback carries is the name it raises, so it goes anywhere in the props and is kept in view state as that
/// name. The page dispatches on the name in its handler of <see cref="ReactComponent.Command"/>, and the browser in its
/// <see cref="ReactComponent.OnClientCommand"/>.
/// </remarks>
public sealed class ReactCommand : ReactValue
{

    /// <summary>
    /// The key of the object a command is kept as in view state, where props are JSON. Reserved there: an object with
    /// this as its only key is read back as a command. Nothing else sees it: the browser and the server render are given
    /// real functions.
    /// </summary>
    internal const string StateKey = "$reactCommand";

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="commandName">The name the callback raises.</param>
    public ReactCommand(string commandName)
    {
        if (string.IsNullOrEmpty(commandName))
            throw new ArgumentException("A command needs a name.", nameof(commandName));

        CommandName = commandName;
    }

    /// <summary>
    /// The name the callback raises.
    /// </summary>
    public string CommandName { get; }

    /// <summary>
    /// Reads a command's view-state object.
    /// </summary>
    /// <param name="element">An object.</param>
    /// <returns>The command it is; <see langword="null"/> where it is not one.</returns>
    internal static ReactCommand? Read(JsonElement element)
    {
        var enumerator = element.EnumerateObject();
        if (enumerator.MoveNext() == false || enumerator.Current.Name != StateKey || enumerator.Current.Value.ValueKind != JsonValueKind.String)
            return null;

        var name = enumerator.Current.Value.GetString();
        if (enumerator.MoveNext() || string.IsNullOrEmpty(name))
            return null;

        return new ReactCommand(name!);
    }

    /// <inheritdoc />
    internal override void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString(StateKey, CommandName);
        writer.WriteEndObject();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return CommandName;
    }

}
