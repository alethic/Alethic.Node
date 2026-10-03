import { ComponentType, ReactNode } from "react";
import { prerender } from "react-dom/static";

/**
 * The server's half: renders a Web Forms page's components to HTML on Node embedded in the worker
 * process, before the page is sent, so the HTML carries them — for anything that reads the page without
 * running it, search engines first.
 *
 * The server bundle exports the `renderOutlets` this makes, built to one self-contained CommonJS file,
 * since the embedded engine resolves nothing but Node's built-ins. `Component` calls it once per
 * page with every component on it, as the page's request: a `fetch` of the site is answered in
 * process, as the visitor, and each callback in the props raises its command on the page.
 *
 * Nothing that fails is passed over. A component that throws, or leaves a rejection of one of its
 * callbacks unhandled, has an error rather than HTML, which the control throws; React would otherwise
 * keep quiet about most of them, leaving the component to the browser.
 */

/**
 * The two of Node's globals this uses, declared here rather than with all of Node's types, which a
 * client's browser code must not see.
 */
declare const process: { on(event: "unhandledRejection", listener: (reason: unknown) => void): void };
declare function setImmediate(callback: (value?: unknown) => void): unknown;

/** What the contexts are made of for one page: called once per render, so nothing is shared between pages. */
export type ProvidersFactory = () => ComponentType<{ children: ReactNode }>;

/** How a page's components are rendered. */
export type RenderOptions = {
    /** Wraps each component, made once per page. */
    providers?: ProvidersFactory;
};

/** A component the page placed, found in this module by its name, and what to render it with. */
export type OutletRequest = {
    id: string;
    component: ComponentType<object>;
    props: Record<string, unknown>;
};

/** Why a component did not render: what was thrown, and where. */
export type RenderError = {
    message: string;
    stack?: string;
    componentStack?: string;

    /** Where a command's handler threw, the id the page keeps what it threw under. */
    dotnetErrorId?: string;
};

/** What became of one component: its HTML, or why there is none. */
export type Rendered = { html: string } | { error: RenderError };

/**
 * Renders a page's components.
 * @param requests the components the page placed
 * @returns what became of each component, by its outlet's id, as JSON
 */
export type RenderOutlets = (requests: OutletRequest[]) => Promise<string>;

/** One page's render: the commands it has called and not had answered, and the rejections left unhandled. */
type Render = {
    pending: number;
    unhandled: Map<string, unknown>;
};

/**
 * Who a command's rejection belongs to: the render and the component whose callback it came from. Kept by
 * the rejection itself, since that is all an unhandled rejection reports, and a component that chained on
 * the callback's promise leaves its own promise unhandled, not the callback's.
 */
const owners = new WeakMap<object, { render: Render; id: string }>();

let listening = false;

/** Starts telling renders of the rejections their components left unhandled, once. */
function listen() {
    if (listening) {
        return;
    }

    listening = true;
    process.on("unhandledRejection", reason => {
        const owner = typeof reason === "object" && reason !== null ? owners.get(reason) : undefined;
        if (owner !== undefined && owner.render.unhandled.has(owner.id) === false) {
            owner.render.unhandled.set(owner.id, reason);
        }
    });
}

/**
 * Props with each function in them, which is a callback raising a command, counted while its answer is
 * awaited and its rejection marked as the component's.
 * @param value the props, or a value in them
 * @param render the page's render
 * @param id the component's outlet
 */
function track(value: unknown, render: Render, id: string): unknown {
    if (typeof value === "function") {
        const callback = value as (...args: unknown[]) => unknown;
        const tracked = (...args: unknown[]) => {
            render.pending++;

            let called: Promise<unknown>;
            try {
                called = Promise.resolve(callback(...args));
            } catch (e) {
                called = Promise.reject(e);
            }

            return called
                .then(v => v, e => {
                    if (typeof e === "object" && e !== null) {
                        owners.set(e, { render, id });
                    }

                    throw e;
                })
                .finally(() => {
                    render.pending--;
                });
        };

        // Named for its command, as the callback is.
        Object.defineProperty(tracked, "name", { value: callback.name });
        return tracked;
    }

    if (Array.isArray(value)) {
        return value.map(i => track(i, render, id));
    }

    if (value !== null && typeof value === "object") {
        return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, track(v, render, id)]));
    }

    return value;
}

/**
 * What was thrown, as the page reports it.
 * @param error what was thrown
 * @param componentStack where in the component tree, where React knows
 */
function describe(error: unknown, componentStack?: string | null): RenderError {
    const dotnetErrorId = (error as { dotnetErrorId?: unknown } | null)?.dotnetErrorId;
    return {
        message: error instanceof Error ? error.message : String(error),
        stack: error instanceof Error ? error.stack : undefined,
        componentStack: componentStack ?? undefined,
        dotnetErrorId: typeof dotnetErrorId === "string" ? dotnetErrorId : undefined,
    };
}

/** A turn of the event loop, after which a rejection left unhandled has been reported. */
function turn() {
    return new Promise(resolve => setImmediate(resolve));
}

/**
 * Makes the `renderOutlets` a server bundle exports.
 * @param options how components are rendered
 */
export function createRenderOutlets(options: RenderOptions = {}): RenderOutlets {
    listen();

    return async requests => {
        const Wrap = options.providers?.() ?? (({ children }: { children: ReactNode }) => <>{children}</>);
        const render: Render = { pending: 0, unhandled: new Map() };
        const rendered: Record<string, Rendered> = {};

        for (const request of requests) {
            const Placed = request.component;
            let error: RenderError | null = null;
            try {
                const props = track(request.props, render, request.id) as object;
                const { prelude } = await prerender(
                    <Wrap>
                        <Placed {...props} />
                    </Wrap>,
                    {
                        // What React recovers from by leaving the component to the browser is still a failure.
                        onError: (e, info) => {
                            error ??= describe(e, info?.componentStack);
                        },
                    });

                const html = await new Response(prelude).text();

                // Every command it called has been answered, and a rejection is reported unhandled once the
                // microtasks it was rejected in have run.
                while (render.pending > 0) {
                    await turn();
                }

                await turn();
                const rejected = render.unhandled.get(request.id);

                rendered[request.id] = error !== null ? { error }
                    : rejected !== undefined ? { error: describe(rejected) }
                    : { html };
            } catch (e) {
                rendered[request.id] = { error: error ?? describe(e) };
            }
        }

        return JSON.stringify(rendered);
    };
}
