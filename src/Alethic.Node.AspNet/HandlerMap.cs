using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Compilation;
using System.Web.Hosting;
using System.Xml.Linq;

namespace Alethic.Node.AspNet;

/// <summary>
/// Finds the handler for a <c>GET</c> of a path, as IIS would have routed it: by the site's
/// <c>system.webServer/handlers</c>.
/// </summary>
/// <remarks>
/// Read from the site's own <c>Web.config</c>, since managed code can read <c>system.webServer</c> neither through
/// <c>System.Configuration</c>, which declares it ignored, nor through IIS's administration API, which needs rights a
/// worker process does not have. So it knows the handlers the site declares — in order, with their <c>remove</c> and
/// <c>clear</c> — and not those it inherits from IIS, save one: a physical <c>.ashx</c> file is compiled, as IIS's simple
/// handler factory would. Read again whenever <c>Web.config</c> changes.
/// </remarks>
public sealed class HandlerMap
{

    static readonly object sync = new();
    static HandlerMap? site;
    static DateTime siteReadAt;

    readonly IReadOnlyList<Mapping> mappings;

    /// <summary>
    /// The map the site's <c>Web.config</c> declares, read again where it has changed since.
    /// </summary>
    public static HandlerMap Site
    {
        get
        {
            var file = HostingEnvironment.MapPath("~/Web.config") ?? throw new InvalidOperationException("The site's handlers can be read only in a hosted ASP.NET application.");
            var writeTime = File.GetLastWriteTimeUtc(file);

            lock (sync)
            {
                if (site is null || writeTime != siteReadAt)
                {
                    site = Parse(XDocument.Load(file));
                    siteReadAt = writeTime;
                }

                return site;
            }
        }
    }

    /// <summary>
    /// The map a configuration file declares, in order, after its <c>remove</c>s and <c>clear</c>s.
    /// </summary>
    /// <param name="config">The configuration file.</param>
    public static HandlerMap Parse(XDocument config)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));

        var mappings = new List<Mapping>();
        var handlers = config.Root?.Element("system.webServer")?.Element("handlers");
        if (handlers is not null)
        {
            foreach (var element in handlers.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "clear":
                        mappings.Clear();
                        break;

                    case "remove":
                        mappings.RemoveAll(i => string.Equals(i.Name, (string?)element.Attribute("name"), StringComparison.OrdinalIgnoreCase));
                        break;

                    case "add":
                        // A handler that is a module, or a script map, has no type, and nothing here can run it.
                        if (string.IsNullOrEmpty((string?)element.Attribute("type")) == false)
                            mappings.Add(new Mapping((string?)element.Attribute("name"), (string?)element.Attribute("path"), (string?)element.Attribute("verb"), (string)element.Attribute("type")!));
                        break;
                }
            }
        }

        return new HandlerMap(mappings);
    }

    HandlerMap(IReadOnlyList<Mapping> mappings)
    {
        this.mappings = mappings;
    }

    /// <summary>
    /// The type a <c>GET</c> of a path is handled by, where one is mapped.
    /// </summary>
    /// <param name="path">The path, relative to the site's root.</param>
    public string? FindType(string path)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        var relative = path.TrimStart('/');
        return mappings.FirstOrDefault(i => i.Matches(relative))?.Type;
    }

    /// <summary>
    /// The handler for a <c>GET</c> of a path, made for a context; <see langword="null"/> where there is none.
    /// </summary>
    /// <param name="context">The context the handler is to run in.</param>
    /// <param name="path">The path, relative to the site's root.</param>
    public IHttpHandler? Find(HttpContext context, string path)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var relative = path.TrimStart('/');
        if (FindType(relative) is string typeName)
        {
            var instance = Activator.CreateInstance(Type.GetType(typeName, true));
            return instance is IHttpHandlerFactory factory
                ? factory.GetHandler(context, "GET", "/" + relative, HostingEnvironment.MapPath("~/" + relative))
                : (IHttpHandler)instance;
        }

        // A handler file, as IIS's simple handler factory compiles it.
        if (relative.EndsWith(".ashx", StringComparison.OrdinalIgnoreCase) && HostingEnvironment.MapPath("~/" + relative) is string file && File.Exists(file))
            return (IHttpHandler)BuildManager.CreateInstanceFromVirtualPath("~/" + relative, typeof(IHttpHandler));

        return null;
    }

    /// <summary>
    /// One handler mapping.
    /// </summary>
    sealed class Mapping
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="name">Its name.</param>
        /// <param name="path">The path it matches: exact, <c>*.ext</c>, or <c>*</c>.</param>
        /// <param name="verb">The verbs it matches: <c>*</c>, or a comma-separated list.</param>
        /// <param name="type">The handler's, or handler factory's, type.</param>
        public Mapping(string? name, string? path, string? verb, string type)
        {
            Name = name;
            Path = path ?? "";
            Verb = verb ?? "*";
            Type = type;
        }

        /// <summary>
        /// Its name.
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// The path it matches.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// The verbs it matches.
        /// </summary>
        public string Verb { get; }

        /// <summary>
        /// The handler's, or handler factory's, type.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Whether it matches a <c>GET</c> of a path: by the whole path, or, as IIS matches a path without a slash, by its
        /// last segment.
        /// </summary>
        /// <param name="relative">The path, relative to the site's root.</param>
        public bool Matches(string relative)
        {
            if (Verb != "*" && Verb.Split(',').Any(i => string.Equals(i.Trim(), "GET", StringComparison.OrdinalIgnoreCase)) == false)
                return false;

            var target = Path.Contains("/") ? relative : relative.Substring(relative.LastIndexOf('/') + 1);
            if (Path == "*")
                return true;
            if (Path.StartsWith("*."))
                return target.EndsWith(Path.Substring(1), StringComparison.OrdinalIgnoreCase);

            return string.Equals(target, Path.TrimStart('/'), StringComparison.OrdinalIgnoreCase);
        }

    }

}
