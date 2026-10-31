import { describe, expect, it } from "vitest";
import { hasTyped, sendTyped } from "../typing.ts";
import { typedBox } from "./typedBox.ts";

function fakeBox(value = ""): HTMLInputElement {
  const box = new EventTarget() as unknown as HTMLInputElement;
  box.value = value;
  return box;
}

describe("typedBox", () => {
  it("sends nothing for a box nobody typed in", () => {
    const sent: string[] = [];
    const action = typedBox(fakeBox("as it was"), (value) => sent.push(value));
    action.destroy();
    expect(sent).toEqual([]);
  });

  it("sends once when the box changes", () => {
    const sent: string[] = [];
    const box = fakeBox();
    const action = typedBox(box, (value) => sent.push(value));
    box.value = "typed";
    box.dispatchEvent(new Event("input"));
    expect(hasTyped()).toBe(true);
    box.dispatchEvent(new Event("change"));
    expect(hasTyped()).toBe(false);
    action.destroy();
    expect(sent).toEqual(["typed"]);
  });

  it("sends what is typed when a save asks, with no blur", () => {
    const sent: string[] = [];
    const box = fakeBox();
    const action = typedBox(box, (value) => sent.push(value));
    box.value = "typed";
    box.dispatchEvent(new Event("input"));
    sendTyped();
    box.dispatchEvent(new Event("change"));
    action.destroy();
    expect(sent).toEqual(["typed"]);
  });

  it("sends what is typed when the box goes away", () => {
    const sent: string[] = [];
    const box = fakeBox();
    const action = typedBox(box, (value) => sent.push(value));
    box.value = "typed";
    box.dispatchEvent(new Event("input"));
    action.destroy();
    expect(sent).toEqual(["typed"]);
    expect(hasTyped()).toBe(false);
  });
});
