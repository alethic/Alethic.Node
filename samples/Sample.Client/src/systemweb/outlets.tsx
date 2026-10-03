import { Component, ComponentType, ReactNode, Suspense, useLayoutEffect, useSyncExternalStore } from "react";
import { createPortal } from "react-dom";
import { createRoot } from "react-dom/client";

/**
 * The browser's half: one React tree for a Web Forms page, with each component the page places rendered
 * into its element through a portal.
 *
 * So every component on the page sits under the same providers — whatever context the root establishes,
 * each of them sees — although they are scattered through markup the server rendered. There is no
 * router: navigating is the page's business.
 *
 * The browser entry exports the `outlet` this makes, beside the components. `Component` writes the
 * import, finds the component in the module by the name the page gave it, and calls `outlet` with it. It
 * calls `remove` on what `outlet` returns when the component's element leaves the page, and `update` when
 * the control is placed again, as a partial postback that renders it again does: the component keeps its
 * place in the tree, so React keeps its state, and is given the new element and props.
 *
 * Where the server rendered the component first, its HTML stays on show until the component has
 * rendered here, and is then replaced by it. Not hydration: the tree is one, rendered through portals,
 * and React does not hydrate a portal. The visitor sees the same markup throughout; what changes is that
 * it is live.
 */

/** What the page's tree is wrapped in: the contexts every component on the page shares. */
export type Providers = ComponentType<{ children: ReactNode }>;

/** How the page's tree is made. */
export type OutletOptions = {
    /** Wraps the page's tree, once. */
    providers?: Providers;

    /** Shown in place of a component that failed. A short notice where not given. */
    fallback?: ReactNode;

    /** Told what a component threw. `console.error` where not given. */
    onError?: (error: unknown) => void;
};

/**
 * Places a component on the page: it renders into the element, as part of the page's one tree.
 * @param component the component
 * @param element the element it appears in
 * @param props its props
 * @returns what removes it again, or moves it to a new element with new props
 */
export type Outlet = (component: ComponentType<object>, element: Element, props: object) => PlacedComponent;

/** A component placed on the page. */
export type PlacedComponent = {
    /** Removes it. */
    remove(): void;

    /** Moves it to a new element, with new props, keeping its state. */
    update(element: Element, props: object): void;
};

/** A place on the page where a component is to appear. */
type Placement = {
    key: number;
    component: ComponentType<object>;
    element: Element;

    /** What the component renders into, out of the document until it has rendered. */
    target: HTMLElement;

    props: object;
};

/**
 * Keeps one component's failure to itself: one tree means it would otherwise take every other component
 * on the page down with it. The fallback is placed as the component would have been.
 */
class Boundary extends Component<{ fallback: ReactNode; placed: ReactNode; onError: (error: unknown) => void; children: ReactNode }, { failed: boolean }> {

    state = { failed: false };

    static getDerivedStateFromError() {
        return { failed: true };
    }

    componentDidCatch(error: unknown) {
        this.props.onError(error);
    }

    render() {
        return this.state.failed ? <>{this.props.fallback}{this.props.placed}</> : this.props.children;
    }

}

/**
 * Puts a placement's box into its element in place of whatever the server rendered there. Rendered beside
 * the component inside its suspense boundary, so this commits only once the component has rendered for
 * real, not while it waits for data.
 * @param props
 */
function Placed({ element, target }: { element: Element; target: HTMLElement }) {
    useLayoutEffect(() => {
        if (target.parentNode !== element) {
            element.replaceChildren(target);
        }
    }, [element, target]);

    return null;
}

/**
 * Makes the page's tree, and the `outlet` that places components in it. The tree is started by the first
 * component placed.
 * @param options how the tree is made
 */
export function createOutlets(options: OutletOptions = {}): Outlet {
    const Wrap = options.providers ?? (({ children }: { children: ReactNode }) => <>{children}</>);
    const fallback = options.fallback ?? <span data-react-error="">This section could not be shown.</span>;
    const onError = options.onError ?? ((error: unknown) => console.error(error));

    let placements: readonly Placement[] = [];
    let lastKey = 0;
    let started = false;
    const listeners = new Set<() => void>();

    function set(next: readonly Placement[]) {
        placements = next;
        listeners.forEach(l => l());
    }

    function Outlets() {
        const current = useSyncExternalStore(
            l => {
                listeners.add(l);
                return () => listeners.delete(l);
            },
            () => placements);

        return current.map(({ key, component: Placed_, element, target, props }) =>
            createPortal(
                <Boundary fallback={fallback} placed={<Placed element={element} target={target} />} onError={onError}>
                    <Suspense fallback={null}>
                        <Placed_ {...props} />
                        <Placed element={element} target={target} />
                    </Suspense>
                </Boundary>,
                target,
                key));
    }

    function start() {
        started = true;

        const container = document.createElement("div");
        container.hidden = true;
        container.setAttribute("data-react-outlets", "");
        document.body.append(container);

        createRoot(container).render(
            <Wrap>
                <Outlets />
            </Wrap>);
    }

    return (component, element, props) => {
        if (started === false) {
            start();
        }

        // Its own box, so the server's HTML in the element stays on show while this renders.
        const target = document.createElement("div");
        target.style.display = "contents";

        let placed: Placement = { key: ++lastKey, component, element, target, props };
        set([...placements, placed]);

        return {
            remove() {
                set(placements.filter(i => i !== placed));
            },
            update(element, props) {
                // The same key and box, so React keeps the component and its state.
                const next = { ...placed, element, props };
                set(placements.map(i => i === placed ? next : i));
                placed = next;
            },
        };
    };
}
