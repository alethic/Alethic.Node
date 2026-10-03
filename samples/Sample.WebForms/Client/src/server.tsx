import { ReactNode } from "react";
import * as components from "./components";
import { Providers } from "./providers";
import { createRenderOutlets } from "./render";

/**
 * The server bundle: `renderOutlets`, over the same components as the browser entry.
 */

export const renderOutlets = createRenderOutlets(components, {
    // Once per page, so one visitor's data is never another's.
    providers: () => {
        const cache = new Map<string, Promise<unknown>>();
        return ({ children }: { children: ReactNode }) => <Providers cache={cache}>{children}</Providers>;
    },
});
