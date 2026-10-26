import { expect, it } from "vitest";
import { sendTyped, typing } from "./typing.ts";

it("sends what every box holds that was typed and not sent, until the box lets go", () => {
  const sent: string[] = [];
  const stop = typing(() => sent.push("life"));
  typing(() => sent.push("speed"))();

  sendTyped();
  stop();
  sendTyped();

  expect(sent).toEqual(["life"]);
});
