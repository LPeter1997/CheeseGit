export interface ParsedStashMessage {
  title: string;
  context: string | null;
}

/**
 * Parse default Git stash subjects like:
 * - "On main: my message"
 * - "WIP on feature/x: my message"
 */
export function formatStashMessage(message: string): ParsedStashMessage {
  const trimmed = message.trim();
  const match = /^(WIP on|On)\s+([^:]+):\s*(.*)$/.exec(trimmed);
  if (!match) {
    return { title: trimmed, context: null };
  }

  const prefix = match[1];
  const branch = match[2].trim();
  const subject = match[3].trim();

  if (!subject) {
    return { title: trimmed, context: null };
  }

  return {
    title: subject,
    context: `${prefix} ${branch}`,
  };
}
