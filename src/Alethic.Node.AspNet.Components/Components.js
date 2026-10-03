// Places the components of Alethic.Node.AspNet.Components' Component controls on a Web Forms page.
//
// Served once per page through WebResource.axd, before the controls' own scripts, each of which is one call:
//
//     AlethicNodeComponents.place({ id, target, module, source, name, props }, onClientCommand?);
//
// - id: the control's element's id, and the key its command's result comes back under.
// - target: the control's UniqueID, which a command posts back to.
// - module: the URL or specifier of the browser's module to import; absent, the page's global scope is the module.
// - source: how the module is named in messages.
// - name: the path to the component in the module, as its parts.
// - props: the component's props, as JSON, each callback an object whose one key is "$componentCommand".
// - onClientCommand: what the control's OnClientCommand evaluates to, where it has one.
//
// The component is handed to the module's outlet, whose returned function is called when the element leaves the page,
// by a partial postback's new markup or by any script, or before a component is placed in the same element again.

(function () {
    "use strict";

    if (window.AlethicNodeComponents) {
        return;
    }

    // The placed components: each element, and the function its module's outlet returned.
    var placed = new Map();

    function remove(element) {
        var removal = placed.get(element);
        placed.delete(element);
        if (typeof removal === "function") {
            try {
                removal();
            } catch (e) {
                console.error(e);
            }
        }
    }

    new MutationObserver(function () {
        placed.forEach(function (removal, element) {
            if (element.isConnected === false) {
                remove(element);
            }
        });
    }).observe(document, { childList: true, subtree: true });

    // Raises a command: posts back to the control, partially where the page has a ScriptManager, and answers with a
    // promise of the command's result, which the partial postback brings back under the control's id. A full postback
    // replaces the page, and the promise with it.
    function dispatch(options, onClientCommand, name, args) {
        if (onClientCommand && onClientCommand(name, args) === false) {
            return Promise.resolve();
        }

        return new Promise(function (resolve, reject) {
            var manager = window.Sys && Sys.WebForms && Sys.WebForms.PageRequestManager && Sys.WebForms.PageRequestManager.getInstance();
            if (manager) {
                var done = function (sender, e) {
                    manager.remove_endRequest(done);

                    var error = e.get_error();
                    if (error) {
                        e.set_errorHandled(true);
                        reject(error);
                        return;
                    }

                    var item = e.get_dataItems()[options.id];
                    resolve(item === undefined ? undefined : JSON.parse(item));
                };

                manager.add_endRequest(done);
            }

            // Off the component's call stack, as the stock controls' AutoPostBack posts back: the PageRequestManager's
            // __doPostBack reads its callers' arguments, which a strict-mode caller refuses.
            var posted = JSON.stringify({ name: name, args: args });
            setTimeout(function () {
                __doPostBack(options.target, posted);
            }, 0);
        });
    }

    // Props with each command's object a function, named for the command, that raises it.
    function callbacks(value, options, onClientCommand) {
        if (Array.isArray(value)) {
            return value.map(function (item) {
                return callbacks(item, options, onClientCommand);
            });
        }

        if (value === null || typeof value !== "object") {
            return value;
        }

        var keys = Object.keys(value);
        if (keys.length === 1 && keys[0] === "$componentCommand" && typeof value.$componentCommand === "string") {
            var name = value.$componentCommand;
            var raise = function () {
                return dispatch(options, onClientCommand, name, Array.prototype.slice.call(arguments));
            };

            Object.defineProperty(raise, "name", { value: name });
            return raise;
        }

        var result = {};
        keys.forEach(function (key) {
            result[key] = callbacks(value[key], options, onClientCommand);
        });

        return result;
    }

    window.AlethicNodeComponents = {

        /**
         * Places a control's component: loads its module, finds the component by name, and hands it to the module's
         * outlet with the control's element and the component's props.
         */
        place: function (options, onClientCommand) {
            var loading = options.module ? import(options.module) : Promise.resolve(globalThis);

            return loading.then(function (module) {
                var source = options.source || "The page's global scope";

                var component = options.name.reduce(function (at, key) {
                    return at == null ? undefined : at[key];
                }, module);

                if (component == null) {
                    throw new Error(source + " has no " + options.name.join(".") + ".");
                }

                if (typeof module.outlet !== "function") {
                    throw new Error(source + " has no outlet function.");
                }

                var element = document.getElementById(options.id);
                var props = callbacks(options.props, options, onClientCommand);

                remove(element);
                placed.set(element, module.outlet(component, element, props));
            });
        },

    };
})();
