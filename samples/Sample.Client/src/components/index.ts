import { CartBadge } from "./CartBadge";
import { ProductCard } from "./ProductCard";

/**
 * The components a page can place, by the name it gives Component's Name: an export here, or a path
 * through one, as `Catalog.ProductCard`.
 */
export { CartBadge } from "./CartBadge";
export { Counter } from "./Counter";
export { Failing } from "./Failing";
export { Greeting } from "./Greeting";
export { ProductCard } from "./ProductCard";
export { PropsView } from "./PropsView";
export { ServerTime } from "./ServerTime";

/** The catalog's components, which the demo names by path. */
export const Catalog = { CartBadge, ProductCard };
