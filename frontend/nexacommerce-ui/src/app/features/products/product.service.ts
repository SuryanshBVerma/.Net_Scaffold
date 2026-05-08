// ═══════════════════════════════════════════════════════════════════════════
// product.service.ts — HTTP data access for products
// ═══════════════════════════════════════════════════════════════════════════
//
// LEARNING — Injectable with providedIn: 'root':
//   The service is a singleton available throughout the app without
//   declaring it in any providers array. Angular's DI tree-shakes it
//   if nothing injects it.
//
//   The base URL is handled by apiBaseUrlInterceptor, so this service
//   uses simple relative paths like '/api/products'.
//
// ═══════════════════════════════════════════════════════════════════════════
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PagedResult, Product } from './product.model';

export interface ProductQuery {
  page?: number;
  pageSize?: number;
  category?: string;
  search?: string;
}

@Injectable({ providedIn: 'root' })
export class ProductService {
  // LEARNING — inject() function (Angular 14+):
  //   Replaces constructor injection. Must be called during construction.
  //   Cleaner for standalone services and functional code.
  private readonly http = inject(HttpClient);

  getProducts(query: ProductQuery = {}): Observable<PagedResult<Product>> {
    let params = new HttpParams()
      .set('page',     query.page     ?? 1)
      .set('pageSize', query.pageSize ?? 20);

    if (query.category) params = params.set('category', query.category);
    if (query.search)   params = params.set('search', query.search);

    return this.http.get<PagedResult<Product>>('/api/products', { params });
  }

  getProduct(id: string): Observable<Product> {
    return this.http.get<Product>(`/api/products/${id}`);
  }
}
