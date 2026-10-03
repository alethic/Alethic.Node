import { ComponentType } from "react";

/**
 * The component a name stands for: an export of the components, or a dotted path through them, as
 * `Catalog.ProductCard`. The browser's `outlet` and the server's `renderOutlets` both resolve names
 * this way, so a page names a component once for both.
 * @param components the components
 * @param name the name a page gave Component's Name
 * @returns the component, or undefined where the name stands for none
 */
export function resolve(components: object, name: string): ComponentType<object> | undefined {
    const found = name.split(".").reduce<unknown>((at, key) => at !== null && typeof at === "object" ? (at as Record<string, unknown>)[key] : undefined, components);
    return typeof found === "function" || (typeof found === "object" && found !== null && "$$typeof" in found)
        ? found as ComponentType<object>
        : undefined;
}
