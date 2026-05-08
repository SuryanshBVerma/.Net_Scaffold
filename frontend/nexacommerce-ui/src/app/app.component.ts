// ═══════════════════════════════════════════════════════════════════════════
// AppComponent — Root shell
// ═══════════════════════════════════════════════════════════════════════════
import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  template: `
    <header>
      <h1>NexaCommerce</h1>
    </header>
    <main>
      <router-outlet />
    </main>
  `,
  styles: [`
    header {
      background: #1a1a2e;
      color: white;
      padding: 1rem 2rem;
    }
    main {
      padding: 2rem;
    }
  `]
})
export class AppComponent {}
