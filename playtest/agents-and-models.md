# Agents and models

Merged on 30 September from [#442](https://github.com/compoodment/ClankerWorld/issues/442), [#443](https://github.com/compoodment/ClankerWorld/issues/443) and [#503](https://github.com/compoodment/ClankerWorld/issues/503).

- Place an adult that uses a personal model, either a founder in a new world or one added with Add Agent. After its first decision, its Profile shows a personality and aspiration in its own words instead of "undecided" and "find a purpose". ([#442](https://github.com/compoodment/ClankerWorld/issues/442))
- Let it make more decisions, then save and load. Its personality and aspiration stay the same. ([#442](https://github.com/compoodment/ClankerWorld/issues/442))
- If a model leaves them out, the agent keeps "undecided" and "find a purpose" and carries on without an extra model call. ([#442](https://github.com/compoodment/ClankerWorld/issues/442))
- Open an agent's Model panel and save a different model. The messages read "Loaded Agent model settings" and "… at the agent's next model choice", without the old words "inhabitant" or "cognition". ([#443](https://github.com/compoodment/ClankerWorld/issues/443))
- Hover over the Send button in an agent's message box. The tooltip reads "Send an instruction to this agent." ([#443](https://github.com/compoodment/ClankerWorld/issues/443))
- Press Speak on an agent's card, choose Order and send something the game can't act on, such as "build a house". The Event Log says the agent didn't understand your order, and the order does not stay waiting. ([#503](https://github.com/compoodment/ClankerWorld/issues/503))
- Send the order "heat the house". It gets the same "didn't understand" line instead of being taken as an order to eat. ([#503](https://github.com/compoodment/ClankerWorld/issues/503))
- With a cloud model, order an agent to eat when it has no food. The order keeps waiting, but the model-call count in World Settings does not climb every second. ([#503](https://github.com/compoodment/ClankerWorld/issues/503))
