# To do

Sven's list, in his order. An item leaves the list when it is done.

The second list (2026-10-27) is built; Sven has not looked at it in the window yet.

1. **Ctrl C on the selected object sometimes does nothing** (Sven, 2026-10-29): seen once, after the dependency pass;
   the context menu's Copy worked, and after a restart Ctrl C did too. Not reproduced yet. Where to look: App's
   `onkeydown` lets Ctrl C go when the page is not the details, the focus is in a box or a popup, or any text is
   selected on the page (a selection left in a tooltip text or a closed dialog would do it); and with `zone` still
   `details` a selected field or total takes the copy instead.
