/**
 * RFC 9457 ProblemDetails, plus the extensions FTMS adds.
 * design: doc 05 section 1 - every failure response has this shape.
 */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly instance?: string;
  readonly traceId?: string;

  /** Present on 400 responses: field name to messages. */
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

export interface ProblemSummary {
  /** Short line for a toast. */
  readonly message: string;

  /** Longer explanation, when the server gave one worth showing. */
  readonly detail?: string;

  /** Field-keyed messages, ready to push into a reactive form. */
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;

  readonly status: number;
  readonly traceId?: string;
}

export function isProblemDetails(body: unknown): body is ProblemDetails {
  return (
    typeof body === 'object' &&
    body !== null &&
    ('title' in body || 'detail' in body || 'type' in body)
  );
}

/**
 * Turns any failure into something showable.
 *
 * The status-code messages are written for a finance user, not a developer: "someone else
 * changed this" beats "412 Precondition Failed" for a person who just wants to fix a date.
 * The traceId is carried along so a support call can be tied to a log line
 * (design: doc 06 section 7 - structured logging with alerting on thresholds).
 */
export function summariseProblem(status: number, body: unknown): ProblemSummary {
  const problem = isProblemDetails(body) ? body : undefined;

  const fallback = messageForStatus(status);
  const message = problem?.title && status !== 0 ? problem.title : fallback;

  return {
    message,
    detail: problem?.detail,
    fieldErrors: problem?.errors ?? {},
    status,
    traceId: problem?.traceId,
  };
}

function messageForStatus(status: number): string {
  switch (status) {
    case 0:
      return 'Could not reach the server';
    case 400:
      return 'Please check the highlighted fields';
    case 401:
      return 'Your session has expired, please sign in again';
    case 403:
      return 'You do not have permission to do that';
    case 404:
      return 'That transaction could not be found';
    case 409:
      return 'That change is not allowed for this transaction';
    case 412:
      return 'Someone else changed this transaction, please reload and try again';
    case 428:
      return 'Reload the transaction before saving';
    case 429:
      return 'Too many requests, please slow down';
    default:
      return status >= 500 ? 'Something went wrong on the server' : 'The request could not be completed';
  }
}
