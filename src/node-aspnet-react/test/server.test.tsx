import { createContext, use, useContext } from "react";
import { describe, expect, it } from "vitest";
import { createRenderOutlets, Rendered } from "../src/server";

/** Renders requests and reads what became of each. */
async function render(renderOutlets: ReturnType<typeof createRenderOutlets>, requests: Parameters<ReturnType<typeof createRenderOutlets>>[0]) {
    return JSON.parse(await renderOutlets(requests)) as Record<string, Rendered>;
}

const Theme = createContext("none");

function Title({ title }: { title: string }) {
    return <h1>{title} ({useContext(Theme)})</h1>;
}

function Throws(): never {
    throw new Error("boom");
}

/** Calls its callback while rendering, and leaves the promise alone. */
function Ignores({ onRender }: { onRender: () => Promise<unknown> }) {
    void onRender().then(() => undefined);
    return <p>ignored</p>;
}

/** Calls its callback while rendering, and catches what it rejects with. */
function Catches({ onRender }: { onRender: () => Promise<unknown> }) {
    onRender().catch(() => undefined);
    return <p>caught</p>;
}

const greeting = Promise.resolve("hello");

/** Waits for data before it renders. */
function Suspends() {
    return <p>{use(greeting)}</p>;
}

const components = { Title, Throws, Ignores, Catches, Suspends };

describe("createRenderOutlets", () => {

    it("renders each component with its props inside the providers", async () => {
        const renderOutlets = createRenderOutlets(components, {
            providers: () => ({ children }) => <Theme.Provider value="dark">{children}</Theme.Provider>,
        });

        const rendered = await render(renderOutlets, [
            { id: "a", component: "Title", props: { title: "One" } },
            { id: "b", component: "Title", props: { title: "Two" } },
        ]);

        expect(rendered.a).toEqual({ html: "<h1>One<!-- --> (<!-- -->dark<!-- -->)</h1>" });
        expect(rendered.b).toEqual({ html: "<h1>Two<!-- --> (<!-- -->dark<!-- -->)</h1>" });
    });

    it("waits for the data a component suspends on", async () => {
        const rendered = await render(createRenderOutlets(components), [{ id: "a", component: "Suspends", props: {} }]);
        expect(rendered.a).toEqual({ html: "<p>hello</p>" });
    });

    it("gives a component that throws an error, with where it was", async () => {
        const rendered = await render(createRenderOutlets(components), [{ id: "a", component: "Throws", props: {} }]);

        expect("error" in rendered.a && rendered.a.error.message).toBe("boom");
        expect("error" in rendered.a && rendered.a.error.componentStack).toContain("Throws");
    });

    it("gives a component the client does not export an error", async () => {
        const rendered = await render(createRenderOutlets(components), [{ id: "a", component: "Missing", props: {} }]);
        expect("error" in rendered.a && rendered.a.error.message).toContain("Missing");
    });

    it("fails a component that leaves a callback's rejection unhandled, carrying its .NET id", async () => {
        const rejection = Object.assign(new Error("handler failed"), { dotnetErrorId: "abc" });

        const rendered = await render(createRenderOutlets(components), [
            { id: "a", component: "Ignores", props: { onRender: () => Promise.reject(rejection) } },
            { id: "b", component: "Catches", props: { onRender: () => Promise.reject(new Error("caught")) } },
        ]);

        expect(rendered.a).toMatchObject({ error: { message: "handler failed", dotnetErrorId: "abc" } });
        expect(rendered.b).toEqual({ html: "<p>caught</p>" });
    });

    it("waits for a callback's answer, however late, before judging the component", async () => {
        const rendered = await render(createRenderOutlets(components), [
            { id: "a", component: "Ignores", props: { onRender: () => new Promise((_, reject) => setTimeout(() => reject(new Error("late")), 50)) } },
        ]);

        expect(rendered.a).toMatchObject({ error: { message: "late" } });
    });

    it("keeps concurrent pages' failures apart", async () => {
        const renderOutlets = createRenderOutlets(components);

        const [first, second] = await Promise.all([
            render(renderOutlets, [{ id: "a", component: "Ignores", props: { onRender: () => Promise.reject(new Error("first")) } }]),
            render(renderOutlets, [{ id: "a", component: "Ignores", props: { onRender: () => Promise.resolve() } }]),
        ]);

        expect(first.a).toMatchObject({ error: { message: "first" } });
        expect(second.a).toEqual({ html: "<p>ignored</p>" });
    });

});
