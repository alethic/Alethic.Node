import { ReactNode } from "react";
import * as components from "../components";
import { createOutlets } from "./outlets";
import { Providers } from "../providers";

/**
 * The browser entry: `outlet`, which places any of the components by the name a page gives it.
 */

/** The page's cache: one tree, so one cache. */
const cache = new Map<string, Promise<unknown>>();

export const outlet = createOutlets(components, {
    providers: ({ children }: { children: ReactNode }) => <Providers cache={cache}>{children}</Providers>,
});
