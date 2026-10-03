import { useCart } from "../providers";

/**
 * The cart's count, in the header. The page gives it the count it had when it rendered; after that,
 * whatever a product card on the page put into the shared context.
 */
export function CartBadge({ count }: { count: number }) {
    const cart = useCart();

    return <span className="badge">Cart: {cart.count ?? count}</span>;
}
