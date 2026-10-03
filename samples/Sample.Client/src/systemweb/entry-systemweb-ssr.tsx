import { ReactNode } from "react";
import { Providers } from "../providers";
import { createRenderOutlets } from "./render";

/**
 * The server bundle: `renderOutlets`, and the same components as the browser entry, which Component
 * finds here by the name the page gives it.
 */

export * from "../components";

export const renderOutlets = createRenderOutlets({
    // Once per page, so one visitor's data is never another's.
    providers: () => {
        const cache = new Map<string, Promise<unknown>>();
        return ({ children }: { children: ReactNode }) => <Providers cache={cache}>{children}</Providers>;
    },
});
