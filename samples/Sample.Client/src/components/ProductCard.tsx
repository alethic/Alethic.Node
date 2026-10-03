import { useState } from "react";
import { useCart } from "../providers";

type Product = { sku: string; name: string; price: number };

/**
 * A product, and a button that adds it to the cart through the page. The page answers with the cart's
 * new count, which goes into the page's shared context: the header's badge, a component elsewhere on the
 * page, shows it at once.
 */
export function ProductCard({ product, onAdd }: { product: Product; onAdd: (sku: string) => Promise<number> }) {
    const { setCount } = useCart();
    const [adding, setAdding] = useState(false);

    return (
        <div className="card product">
            <h3>{product.name}</h3>
            <p><code>{product.sku}</code> — ${product.price.toFixed(2)}</p>
            <button type="button" disabled={adding} onClick={async () => {
                setAdding(true);
                try {
                    setCount(await onAdd(product.sku));
                } finally {
                    setAdding(false);
                }
            }}>
                {adding ? "Adding…" : "Add to cart"}
            </button>
        </div>
    );
}
