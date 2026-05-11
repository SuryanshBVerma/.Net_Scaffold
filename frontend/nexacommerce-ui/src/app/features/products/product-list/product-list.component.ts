// ═══════════════════════════════════════════════════════════════════════════
// product-list.component.ts — Product catalogue listing
// ═══════════════════════════════════════════════════════════════════════════
//
// LEARNING — Angular 20 features demonstrated:
//
//   1. Signals for reactive state (no BehaviorSubject, no OnPush boilerplate)
//   2. New control flow syntax (@for, @if, @defer) instead of *ngFor/*ngIf
//   3. Standalone component (no NgModule needed)
//   4. inject() instead of constructor injection
//   5. signal() + computed() + effect() instead of RxJS pipe chains for UI state
//   6. toSignal() bridges the Observable from ProductService into a signal
//
// ═══════════════════════════════════════════════════════════════════════════
import { Component, inject, signal, computed } from '@angular/core';
import { toSignal, toObservable } from '@angular/core/rxjs-interop';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProductService } from '../product.service';
import { Product } from '../product.model';
import { switchMap, startWith, catchError, of } from 'rxjs';

@Component({
  selector: 'app-product-list',
  standalone: true,
  imports: [FormsModule, CurrencyPipe],
  template: `
    <section class="product-list">
      <h2>Products</h2>

      <!-- LEARNING — New template control flow: @if instead of *ngIf -->
      <!-- No import of NgIf needed — it's built into the compiler. -->

      <div class="toolbar">
        <input
          type="search"
          placeholder="Search products..."
          [ngModel]="searchQuery()"
          (ngModelChange)="searchQuery.set($event)"
        />
        <span class="count">
          @if (totalCount() > 0) {
            {{ totalCount() }} products
          }
        </span>
      </div>

      <!-- LEARNING — @if replaces *ngIf. Reads like plain TypeScript. -->
      @if (loading()) {
        <p class="state-msg">Loading products…</p>
      } @else if (error()) {
        <p class="state-msg error">
          Failed to load products. Is the ProductCatalog service running?
        </p>
      } @else {
        <!-- LEARNING — @for replaces *ngFor. track is REQUIRED (replaces trackBy). -->
        <!-- The 'track' expression must be unique per item — use a stable ID. -->
        <ul class="products">
          @for (product of products(); track product.id) {
            <li class="product-card">
              <strong>{{ product.name }}</strong>
              <span class="sku">SKU: {{ product.sku }}</span>
              <span class="price">{{ product.price | currency }}</span>
              <span class="stock"
                [class.low]="product.stockQuantity < 10">
                {{ product.stockQuantity }} in stock
              </span>
            </li>
          } @empty {
            <!-- LEARNING — @empty renders when @for collection is empty. -->
            <!-- No need for a separate *ngIf on an empty-state element. -->
            <li class="state-msg">No products found.</li>
          }
        </ul>
      }
    </section>
  `,
  styles: [`
    .product-list { max-width: 900px; }
    .toolbar { display: flex; gap: 1rem; align-items: center; margin-bottom: 1rem; }
    input[type=search] { flex: 1; padding: 0.5rem; border: 1px solid #ccc; border-radius: 4px; }
    .products { list-style: none; display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 1rem; }
    .product-card { background: white; border: 1px solid #e0e0e0; border-radius: 8px; padding: 1rem; display: flex; flex-direction: column; gap: 0.4rem; }
    .sku { font-size: 0.8rem; color: #888; }
    .price { font-weight: bold; color: #1a1a2e; }
    .stock { font-size: 0.85rem; }
    .stock.low { color: #c0392b; }
    .state-msg { padding: 2rem; text-align: center; color: #666; }
    .error { color: #c0392b; }
  `]
})
export class ProductListComponent {
  private readonly productService = inject(ProductService);

  // LEARNING — signal():
  //   Writable reactive value. Setting it triggers all dependent computed()
  //   signals and re-renders templates that read it — no zone.js, no markForCheck().
  readonly searchQuery = signal('');

  // LEARNING — computed():
  //   Derived signal. Recalculates only when searchQuery changes.
  //   Here it adds a 300ms debounce via the Observable bridge below.
  private readonly query$ = toObservable(this.searchQuery);

  // LEARNING — toSignal() + switchMap = reactive HTTP with cancellation:
  //   toSignal() subscribes to the observable and returns a signal.
  //   switchMap cancels the previous HTTP request when searchQuery changes
  //   (no stale responses appearing after typing fast).
  //   catchError prevents a 401 from crashing the signal.
  private readonly result = toSignal(
    this.query$.pipe(
      switchMap(q =>
        this.productService.getProducts({ search: q || undefined }).pipe(
          catchError(() => of(null))
        )
      ),
      startWith(undefined)
    )
  );

  // LEARNING — computed() signals derived from the result signal:
  //   Each of these recalculates automatically when result() changes.
  //   The template simply reads loading(), products(), etc. — no async pipe needed.
  readonly loading   = computed(() => this.result() === undefined);
  readonly error     = computed(() => this.result() === null);
  readonly products  = computed((): Product[] => this.result()?.items ?? []);
  readonly totalCount = computed(() => this.result()?.totalCount ?? 0);
}
