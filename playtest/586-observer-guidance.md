# Speaking to agents

Pending paired Windows client/server checks for [#586](https://github.com/compoodment/ClankerWorld/issues/586).

- Pair the Godot client to a world with an adult assigned to a personal planning model. If Jev is enabled for routine choices, send a Suggest and confirm the agent's next personal planning request receives the exact words as an outside-observer message, not as a thought or another agent's speech.
- Wait for the model's next accepted request. The Profile's **Your messages** section should show the original words and that the personal model heard them. If it returns a short reply, it should appear under **Agent reply**, separate from the Thoughts reader.
- Send a recognized Order to eat one carried food item. With food carried, the agent should attempt that action before its preferences and the Event Log should show progress only after the food is actually eaten. Without carried food, the order should remain open; model calls should follow the usual cadence instead of repeating every tick.
- With an Order still waiting (for example, eat with no food carried), send four Orders the game can't act on, such as "build a house". Each should appear under **Your messages** as closed and not reported as heard, and the waiting Order should stay visible on the card.
- Send a Suggest, then a recognized Order before a personal model is configured. The order may progress through local rules, while the Suggest remains unreported as heard. The card must not claim that a deterministic choice was a personal-model response.
- Configure the adult's planning role as deterministic and repeat. No hosted request should be forced by the message. For a child with no explicit model assignment, recognized orders still use legal local choices and no world-default paid request is made.
- Pause while a personal-model request is pending, save and reload, then resume. The reply must remain bound to the exact world, agent and message ID; it must not be attached to a newer message. Verify the pending words remain visible on the same agent's card.
