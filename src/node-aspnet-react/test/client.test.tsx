// @vitest-environment jsdom

import { act, createContext, useContext, useEffect } from "react";
import { afterEach, beforeAll, describe, expect, it } from "vitest";
import { createOutlets } from "../src/client";

beforeAll(() => {
    (globalThis as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;
});

afterEach(() => {
    document.body.replaceChildren();
});

/** An element on the page, holding what the server rendered. */
function element(serverHtml = "") {
    const div = document.createElement("div");
    div.innerHTML = serverHtml;
    document.body.append(div);
    return div;
}

const Theme = createContext("none");

function Title({ title }: { title: string }) {
    return <h1>{title} ({useContext(Theme)})</h1>;
}

function Throws(): never {
    throw new Error("boom");
}

describe("createOutlets", () => {

    it("renders each component into its element, inside the providers, in place of the server's HTML", async () => {
        const outlet = createOutlets({ providers: ({ children }) => <Theme.Provider value="dark">{children}</Theme.Provider> });
        const one = element("<h1>server</h1>");
        const two = element();

        await act(async () => {
            outlet(Title, one, { title: "One" });
            outlet(Title, two, { title: "Two" });
        });

        expect(one.textContent).toBe("One (dark)");
        expect(two.textContent).toBe("Two (dark)");
    });

    it("keeps one component's failure to itself", async () => {
        const errors: unknown[] = [];
        const outlet = createOutlets({ fallback: <em>failed</em>, onError: e => errors.push(e) });
        const failing = element();
        const fine = element();

        await act(async () => {
            outlet(Throws, failing, {});
            outlet(Title, fine, { title: "Fine" });
        });

        expect(failing.textContent).toBe("failed");
        expect(fine.textContent).toBe("Fine (none)");
        expect(errors).toHaveLength(1);
    });

    it("removes a component whose element leaves the page", async () => {
        let mounted = false;
        function Tracks() {
            useEffect(() => {
                mounted = true;
                return () => {
                    mounted = false;
                };
            }, []);
            return <p>here</p>;
        }

        const outlet = createOutlets();
        const placed = element();

        await act(async () => {
            outlet(Tracks, placed, {});
        });
        expect(mounted).toBe(true);

        await act(async () => {
            placed.remove();
            await new Promise(resolve => setTimeout(resolve, 0));
        });
        expect(mounted).toBe(false);
    });

    it("replaces a component placed again in the same element", async () => {
        const outlet = createOutlets();
        const placed = element();

        await act(async () => {
            outlet(Title, placed, { title: "First" });
        });
        await act(async () => {
            outlet(Title, placed, { title: "Second" });
        });

        expect(placed.textContent).toBe("Second (none)");
    });

    it("refuses a component the client does not export", () => {
        expect(() => createOutlets()(undefined, element(), {})).toThrow("no such component");
    });

});
