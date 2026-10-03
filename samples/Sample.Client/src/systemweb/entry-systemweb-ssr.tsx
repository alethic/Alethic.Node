import { ComponentType } from "react";
import { prerender } from "react-dom/static";
import { Providers } from "../providers";

/**
 * The server module: `render`, and the same components as the browser entry, which Component finds here
 * by the name the page gives it.
 */

export * from "../components";

/**
 * Renders one component to HTML, or throws. The library calls it for each component on a page, one
 * after another, with the same `state` object, and does the rest: the callbacks, waiting for their
 * answers, and reporting what failed.
 * @param Component the component
 * @param props its props
 * @param state the page render's own object, shared by its components
 */
export async function render(Component: ComponentType<object>, props: object, state: { cache?: Map<string, Promise<unknown>> }) {
    // One cache for the page, as in the browser's one tree, so its components ask once.
    state.cache ??= new Map();

    // What React recovers from by leaving the component to the browser, it tells only onError: that is still a failure.
    let failure: unknown;
    const { prelude } = await prerender(
        <Providers cache={state.cache}>
            <Component {...props} />
        </Providers>,
        {
            onError: (e, info) => {
                failure ??= e instanceof Error ? Object.assign(e, { componentStack: info?.componentStack }) : e;
            },
        });

    if (failure !== undefined) {
        throw failure;
    }

    return await new Response(prelude).text();
}
