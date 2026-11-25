import { ApiProblem } from '../../core/http/problem.interceptor';
import { I18n, TranslateParams } from '../../core/i18n/i18n';

/** A message for a live region. Catalog ids re-translate on a language switch; plain text is final. */
export interface Notice {
  readonly id?: string;
  readonly params?: TranslateParams;
  readonly text?: string;
}

/** Rate limits keep a live countdown; other API failures already carry their localized message. */
export function noticeFromError(error: unknown): Notice {
  if (error instanceof ApiProblem) {
    if (error.status === 429 && error.retryAfterSeconds !== undefined) {
      return { id: 'errors.rateLimited', params: { seconds: error.retryAfterSeconds } };
    }
    return { text: error.message };
  }
  return { id: 'errors.unexpected' };
}

export function noticeText(i18n: I18n, notice: Notice): string {
  return notice.text ?? i18n.t(notice.id ?? 'errors.unexpected', notice.params);
}
