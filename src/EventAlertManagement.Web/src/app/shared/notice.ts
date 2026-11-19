import { Component, input } from '@angular/core';

/** Shows what went wrong (or right) in one consistent place. */
@Component({
  selector: 'app-notice',
  template: `
    @if (errors().length > 0) {
      <div class="notice error" role="alert">
        @if (errors().length === 1) { {{ errors()[0] }} } @else { <ul>@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
      </div>
    }
    @if (message()) { <div class="notice ok" role="status">{{ message() }}</div> }
  `,
})
export class Notice {
  readonly errors = input<string[]>([]);
  readonly message = input<string>('');
}
