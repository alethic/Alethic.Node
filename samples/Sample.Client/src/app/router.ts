/**
 * The application's routes.
 *
 * One table, and the only one: App renders from it, the server entry dispatches on it, and the host
 * reads it through a route provider to build its endpoint table. A route added here is a route
 * everywhere — which is the point of the host reading the real router rather than being handed a
 * separate description of it that nothing keeps honest.
 *
 * `render` is this router's own per-route policy, of the sort real routers carry. The provider on the
 * .NET side translates it into the host's RenderMode; the application itself knows nothing about that
 * type.
 */
export type Route = { id: string; path: string; render: "server" | "prerender" };

export const routes: Route[] = [
    { id: "home", path: "/", render: "server" },
    { id: "about", path: "/about", render: "prerender" },
    { id: "park", path: "/parks/:parkRef", render: "server" },
];

/** A route a path matched, and its parameters. */
export type Match = { route: Route; params: Record<string, string> };

/**
 * Matches a pathname against the table, answering the route and its parameters, or null.
 *
 * Deliberately tiny — a real application has its framework's matcher here. What matters to the
 * sample is that this is the matcher the application actually dispatches with.
 * @param pathname the path
 */
export function match(pathname: string): Match | null {
    const parts = pathname.split("/").filter(Boolean);

    for (const route of routes) {
        const segments = route.path.split("/").filter(Boolean);
        if (segments.length !== parts.length) {
            continue;
        }

        const params: Record<string, string> = {};
        let matched = true;

        for (let i = 0; i < segments.length; i++) {
            if (segments[i].startsWith(":")) {
                params[segments[i].slice(1)] = decodeURIComponent(parts[i]);
            } else if (segments[i] !== parts[i]) {
                matched = false;
                break;
            }
        }

        if (matched) {
            return { route, params };
        }
    }

    return null;
}
