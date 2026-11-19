import { Component, computed, input, output } from '@angular/core';

@Component({
  selector: 'app-pager',
  template: `
    @if (pages() > 1) {
      <div class="row small" style="margin-top:.75rem; align-items:center">
        <button type="button" (click)="go.emit(page() - 1)" [disabled]="page() <= 1">Previous</button>
        <span>Page {{ page() }} of {{ pages() }} ({{ total() }} in total)</span>
        <button type="button" (click)="go.emit(page() + 1)" [disabled]="page() >= pages()">Next</button>
      </div>
    }
  `,
})
export class Pager {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly go = output<number>();
  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
}
