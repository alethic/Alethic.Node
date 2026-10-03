import { createContext, ReactNode, use, useContext, useState } from "react";

/**
 * What every component on a page shares: one tree's worth of context, whether the tree is the browser's
 * one root or the server's render of the page.
 */

/** Data fetched for the page, by key, so a component that renders again asks once. */
const CacheContext = createContext<Map<string, Promise<unknown>> | null>(null);

/** The visitor's cart count, which the header's badge shows and every product card changes. */
const CartContext = createContext<{ count: number | null; setCount: (count: number) => void }>({ count: null, setCount: () => { } });

/**
 * Wraps a page's components.
 * @param props
 */
export function Providers({ cache, children }: { cache: Map<string, Promise<unknown>>; children: ReactNode }) {
    const [count, setCount] = useState<number | null>(null);

    return (
        <CacheContext value={cache}>
            <CartContext value={{ count, setCount }}>
                {children}
            </CartContext>
        </CacheContext>
    );
}

/**
 * What a promise resolves to, asked for once per page under a key: suspends until it is there.
 * @param key what it is
 * @param load asks for it
 */
export function useCached<T>(key: string, load: () => Promise<T>): T {
    const cache = useContext(CacheContext);
    if (cache === null) {
        throw new Error("useCached needs the page's Providers.");
    }

    let promise = cache.get(key) as Promise<T> | undefined;
    if (promise === undefined) {
        promise = load();
        cache.set(key, promise);
    }

    return use(promise);
}

/** The visitor's cart count, shared across the page. */
export function useCart() {
    return useContext(CartContext);
}
