// Synthetic Codex rollout lines for tests. The record shapes follow docs/agent-time-spec.md
// (measured key by key on a real rollout); every value is invented: no real path, id or text.

export type Line = Record<string, unknown>;

let ordinal = 0;
const at = (iso: string, type: string, payload: Line, metadata?: Line): Line => ({
  timestamp: iso,
  ordinal: ordinal++,
  type,
  payload,
  ...(metadata ? { metadata } : {}),
});

export function sessionMeta(iso: string, o: { sessionId: string; threadSource: string; cwd: string; subagent?: boolean }): Line {
  return at(iso, 'session_meta', {
    // Credential-shaped fields are present on purpose: the reader must never carry them anywhere.
    creator_user_id: 'fixture-user-0001',
    creator_account_id: 'fixture-account-0001',
    session_id: o.sessionId,
    id: o.sessionId,
    timestamp: iso,
    cwd: o.cwd,
    runtime_workspace_roots: [o.cwd],
    originator: 'Codex Desktop',
    cli_version: '0.0.0-fixture',
    source: o.subagent ? { subagent: { other: 'fixture' } } : 'vscode',
    thread_source: o.threadSource,
    model_provider: 'fixture',
    base_instructions: { text: 'FIXTURE-INSTRUCTIONS', provenance: { type: 'fixture' } },
  });
}

export function turnContext(iso: string, cwd: string): Line {
  return at(iso, 'turn_context', {
    turn_id: 'turn-fixture',
    cwd,
    workspace_roots: [cwd],
    current_date: iso.slice(0, 10),
    timezone: 'UTC',
  });
}

const textContent = (kind: string, text: string): Line[] => [{ type: kind, text }];

/** A user message Codex injected (instructions, context): no metadata at all. */
export function injectedUser(iso: string, text = 'FIXTURE-INJECTED-CONTEXT'): Line {
  return at(iso, 'response_item', { type: 'message', id: 'msg-injected', role: 'user', content: textContent('input_text', text) });
}

/** The human's prompt: a user message carrying `metadata.user_input_order`. */
export function humanPrompt(iso: string, order: number, text = 'FIXTURE-PROMPT-TEXT'): Line {
  return at(
    iso,
    'response_item',
    { type: 'message', id: `msg-prompt-${order}`, role: 'user', content: textContent('input_text', text) },
    {
      retained_source: { id: { message_id: 'm', turn_id: 't', role: 'user' }, revision: 'r', complete: true },
      client_authored: false,
      user_input_order: order,
      mcp_attribution: { status: 'none' },
    },
  );
}

/** A user message with only `mcp_attribution`, the one injected shape that had metadata. */
export function attributedUser(iso: string): Line {
  return at(
    iso,
    'response_item',
    { type: 'message', id: 'msg-attr', role: 'user', content: textContent('input_text', 'FIXTURE-ATTRIBUTED') },
    { mcp_attribution: { status: 'none' } },
  );
}

export function reasoning(iso: string): Line {
  return at(iso, 'response_item', { type: 'reasoning', id: 'rs', summary: [], encrypted_content: 'FIXTURE-ENCRYPTED' });
}

export function assistant(iso: string, text = 'FIXTURE-ANSWER'): Line {
  return at(
    iso,
    'response_item',
    { type: 'message', id: 'msg-a', role: 'assistant', content: textContent('output_text', text), phase: 'final' },
    { client_authored: false, user_input_order: 1 },
  );
}

export function developer(iso: string): Line {
  return at(iso, 'response_item', { type: 'message', id: 'msg-d', role: 'developer', content: textContent('input_text', 'FIXTURE-DEV') });
}

export function agentMessage(iso: string): Line {
  return at(iso, 'response_item', { type: 'agent_message', id: 'am', author: 'a', recipient: 'b', content: textContent('text', 'FIXTURE-AGENT-MESSAGE') });
}

export function functionCall(iso: string, name = 'wait', args = '{}'): Line {
  return at(iso, 'response_item', { type: 'function_call', id: 'fc', name, arguments: args, call_id: 'call-fc' });
}

export function functionCallOutput(iso: string): Line {
  return at(iso, 'response_item', { type: 'function_call_output', id: 'fco', call_id: 'call-fc', output: textContent('input_text', 'FIXTURE-OUTPUT') });
}

export function customToolCall(iso: string, name: string, input: string): Line {
  return at(iso, 'response_item', { type: 'custom_tool_call', id: 'ctc', status: 'completed', call_id: 'call-ctc', name, input });
}

export function customToolCallOutput(iso: string): Line {
  return at(iso, 'response_item', { type: 'custom_tool_call_output', id: 'ctco', call_id: 'call-ctc', output: textContent('input_text', 'FIXTURE-TOOL-OUTPUT') });
}

export function taskStarted(iso: string): Line {
  return at(iso, 'event_msg', { type: 'task_started', turn_id: 'turn-fixture', started_at: 0, model_context_window: 1 });
}

export function itemCompleted(iso: string, itemType: string): Line {
  return at(iso, 'event_msg', {
    type: 'item_completed',
    thread_id: 'thread-fixture',
    turn_id: 'turn-fixture',
    item: { type: itemType, id: 'item', content: textContent('text', 'FIXTURE-ITEM-TEXT') },
  });
}

export function tokenCount(iso: string): Line {
  return at(iso, 'event_msg', { type: 'token_count', info: { total_token_usage: { total_tokens: 1 } } });
}

export function worldState(iso: string): Line {
  return at(iso, 'world_state', { full: true, state: {} });
}

/**
 * A two-file patch: `src/a.ts` updated (2 added, 1 removed, context and a hunk header that must
 * not count) and `src/new.ts` added (3 lines). Paths are relative to the turn's cwd.
 */
export const TWO_FILE_PATCH = [
  '*** Begin Patch',
  '*** Update File: src/a.ts',
  '@@ function fixture()',
  ' const keep = 1;',
  '-const old = 2;',
  '+const fresh = 2;',
  '+const extra = 3;',
  ' const keep2 = 4;',
  '*** Add File: src/new.ts',
  '+export const one = 1;',
  '+export const two = 2;',
  '+export const three = 3;',
  '*** End Patch',
].join('\n');

/** Codex's `exec` tool quoting a patch inside JS, newlines escaped, as Codex Desktop sends it. */
export function execWithPatch(patch: string): string {
  return `const r = await tools.apply_patch(${JSON.stringify(patch)});\ntext(r);\n`;
}

export const toJsonl = (lines: readonly Line[]): string => lines.map((l) => JSON.stringify(l)).join('\n') + '\n';
