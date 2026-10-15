import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { ConfirmDialog, ConfirmDialogData } from './confirm-dialog';

function render(data: ConfirmDialogData): HTMLElement {
  TestBed.configureTestingModule({
    imports: [ConfirmDialog],
    providers: [
      provideTranslateService(),
      { provide: MAT_DIALOG_DATA, useValue: data },
      { provide: MatDialogRef, useValue: { close: vi.fn() } },
    ],
  });
  const fixture = TestBed.createComponent(ConfirmDialog);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('ConfirmDialog', () => {
  it('renders title, message and the custom confirm label', () => {
    const element = render({ titleKey: 't.title', messageKey: 't.message', confirmKey: 'common.delete' });

    expect(element.textContent).toContain('t.title');
    expect(element.textContent).toContain('t.message');
    expect(element.textContent).toContain('common.delete');
  });

  it('uses the default confirm label', () => {
    const element = render({ titleKey: 't.title', messageKey: 't.message' });

    expect(element.textContent).toContain('common.confirm');
  });
});
