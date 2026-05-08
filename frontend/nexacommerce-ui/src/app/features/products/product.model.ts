// ═══════════════════════════════════════════════════════════════════════════
// product.model.ts — Domain model for the product list feature
// ═══════════════════════════════════════════════════════════════════════════
export interface Product {
  id: string;
  name: string;
  description: string;
  price: number;
  stockQuantity: number;
  sku: string;
  category: string;
  imageUrl: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
