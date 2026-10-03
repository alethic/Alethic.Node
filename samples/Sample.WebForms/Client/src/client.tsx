import { ReactNode } from "react";
import { createOutlets } from "./outlets";
import { Providers } from "./providers";

/**
 * The browser entry: `outlet`, and every component a page may place.
 */

export * from "./components";

/** The page's cache: one tree, so one cache. */
const cache = new Map<string, Promise<unknown>>();

export const outlet = createOutlets({
    providers: ({ children }: { children: ReactNode }) => <Providers cache={cache}>{children}</Providers>,
});
