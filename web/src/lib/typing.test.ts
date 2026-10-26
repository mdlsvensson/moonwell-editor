import { expect, it } from "vitest";
import { hasTyped, sendTyped, typing } from "./typing.ts";

it("sends what every box holds that was typed and not sent, until the box lets go", () => {
  const sent: string[] = [];
  const stop = typing(() => sent.push("life"));
  typing(() => sent.push("speed"))();

  sendTyped();
  stop();
  sendTyped();

  expect(sent).toEqual(["life"]);
});

it("knows whether any box holds a typed value", () => {
  expect(hasTyped()).toBe(false);
  const stop = typing(() => {});
  expect(hasTyped()).toBe(true);
  stop();
  expect(hasTyped()).toBe(false);
});
