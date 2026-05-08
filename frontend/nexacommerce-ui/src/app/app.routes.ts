import { Routes } from '@angular/router';
import { ProductListComponent } from './features/products/product-list/product-list.component';

export const routes: Routes = [
  { path: '', redirectTo: 'products', pathMatch: 'full' },
  { path: 'products', component: ProductListComponent },
];
