/**
 * Tab-separated text, the way spreadsheets copy and paste a block of cells: a row per line, a tab between cells,
 * and a cell that holds a tab, a line break or a quote wrapped in quotes (a quote inside doubled).
 */

/**
 * The rows and cells of copied text. Lines end with `\n` or `\r\n`; the one line break at the very end that a
 * spreadsheet adds is not a row. Empty text has no rows. An empty cell is `""`. A quote opens a quoted cell only at
 * the start of a cell.
 */
export function parseTsv(text: string): string[][] {
  if (text === "") return [];
  let body = text;
  if (body.endsWith("\r\n")) body = body.slice(0, -2);
  else if (body.endsWith("\n")) body = body.slice(0, -1);

  const rows: string[][] = [];
  let row: string[] = [];
  let cell = "";
  let atCellStart = true;
  let inQuotes = false;
  let at = 0;
  while (at < body.length) {
    const char = body[at];
    if (inQuotes) {
      if (char === '"' && body[at + 1] === '"') {
        cell += '"';
        at += 2;
      } else {
        if (char === '"') inQuotes = false;
        else cell += char;
        at++;
      }
    } else if (char === '"' && atCellStart) {
      inQuotes = true;
      atCellStart = false;
      at++;
    } else if (char === "\t") {
      row.push(cell);
      cell = "";
      atCellStart = true;
      at++;
    } else if (char === "\n" || (char === "\r" && body[at + 1] === "\n")) {
      row.push(cell);
      rows.push(row);
      row = [];
      cell = "";
      atCellStart = true;
      at += char === "\r" ? 2 : 1;
    } else {
      cell += char;
      atCellStart = false;
      at++;
    }
  }
  row.push(cell);
  rows.push(row);
  return rows;
}

/** A cell as copied: in quotes when it holds a tab, a line break or a quote. */
function formatCell(cell: string): string {
  if (/[\t\n\r"]/.test(cell)) return `"${cell.replaceAll('"', '""')}"`;
  return cell;
}

/** Rows as copied text: a tab between cells and a `\n` after each row, so `parseTsv` reads it back unchanged. */
export function formatTsv(rows: string[][]): string {
  return rows.map((row) => `${row.map(formatCell).join("\t")}\n`).join("");
}
