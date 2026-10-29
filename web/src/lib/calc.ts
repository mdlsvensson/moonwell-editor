/**
 * Arithmetic typed into a number box (Sven, 2026-10-29): `10*2+5`, `(3+4)/2`, `-5`, worked out when the box sends its
 * value. A box that starts with `*` or `/` works on the value it holds (`*2` doubles it); `+` and `-` in front are a
 * number's sign. Only numbers, `+ - * /` (and `x` or `×` for times) and brackets are read.
 */

/** The number the text works out to; null when it is not arithmetic, or divides by 0. */
export function calculate(text: string, current = 0): number | null {
  let typed = text.trim().replace(/[x×]/g, "*").replace(/,/g, ".");
  if (typed === "") return null;
  if (typed.startsWith("*") || typed.startsWith("/")) typed = `${current}${typed}`;
  const tokens = typed.match(/\d+\.?\d*|\.\d+|[-+*/()]|\S/g) ?? [];
  let at = 0;

  // sum = product (('+' | '-') product)*; product = unary (('*' | '/') unary)*; unary = ('+' | '-') unary | atom
  function sum(): number | null {
    let value = product();
    while (value !== null && (tokens[at] === "+" || tokens[at] === "-")) {
      const op = tokens[at++];
      const right = product();
      if (right === null) return null;
      value = op === "+" ? value + right : value - right;
    }
    return value;
  }

  function product(): number | null {
    let value = unary();
    while (value !== null && (tokens[at] === "*" || tokens[at] === "/")) {
      const op = tokens[at++];
      const right = unary();
      if (right === null || (op === "/" && right === 0)) return null;
      value = op === "*" ? value * right : value / right;
    }
    return value;
  }

  function unary(): number | null {
    if (tokens[at] === "-" || tokens[at] === "+") {
      const sign = tokens[at++] === "-" ? -1 : 1;
      const value = unary();
      return value === null ? null : sign * value;
    }
    if (tokens[at] === "(") {
      at++;
      const value = sum();
      if (tokens[at] !== ")") return null;
      at++;
      return value;
    }
    const token = tokens[at];
    if (token === undefined || !/^(\d+\.?\d*|\.\d+)$/.test(token)) return null;
    at++;
    return Number(token);
  }

  const value = sum();
  return value !== null && at === tokens.length && Number.isFinite(value) ? value : null;
}

/** True when the text is more than a plain number: arithmetic to work out. */
export function isArithmetic(text: string): boolean {
  return !/^\s*[-+]?(\d+\.?\d*|\.\d+)\s*$/.test(text) && /[-+*/x×()]/.test(text);
}
