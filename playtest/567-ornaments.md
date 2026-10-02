# Gold and diamond ornaments

Pending Windows game checks for [#567](https://github.com/compoodment/ClankerWorld/issues/567). This draft is intended to connect actual rare-material gathering, refining, crafting, wearing, gifts and barter. Normal runtime checks, review and merge are still pending; no hands-on pass is claimed.

- Watch an adult obtain and carry a usable iron pickaxe, walk to a reachable finite gold or diamond outcrop and mine a complete load. A missing or reserved tool, blocked route or full load should leave the deposit and tool unchanged.
- Carry gold ore and fuel to the household Blacksmith. Refine gold, make a gold ornament and optionally set a real diamond. Stock at another House or a distant Blacksmith must not count as ingredients. Watch the actual inputs decrease and the finished good appear at the workplace.
- Save and reopen while inputs and output room are reserved, and again while setting the diamond. The same job should finish without spending ingredients or producing an ornament twice.
- Collect and wear the actual ornament. Keep its recorded owner and quantity; taking it off should leave it in carried stock. Wearing must not improve warmth, carrying room or combat ability, and the map appearance should stay the same.
- Give a named nearby agent an ornament. Watch the giver reach the recipient and transfer one real unit. A remote recipient, full recipient load, reserved ornament or another person's goods must not transfer. Save and reopen after the gift, and confirm that the former wearer no longer has it selected.
- Let a fresh personal-model choice decide whether to put on, remove or give an ornament. Jev, a failed reply, a repeated intention or an owner order must not trigger those decisions or give the ornament away.
- Buy an ornament from another household's Blacksmith, or offer a personally owned spare through barter. Both parties must complete the existing physical exchange. Purchased goods go to the buyer; shop payment remains at that shop. The buyer gains no private-stock or household access.
- Cancel a pending exchange and inspect both owners' goods. Worn ornaments must not be unloaded or offered automatically as payment. Save and reopen during the exchange without duplicating goods or reservations.
- Check the inventory names, agent's worn-item description and Event Log messages. New textures and changes to the agent's appearance belong to the separate art work.
- Check that death and estate handling preserve the actual ornament as property without retaining an active worn-item selection on the deceased profile.

Keep a backup of older worlds. The provisional ornament format is schema 43 above its Clinic parent; older alpha saves are refused and left unchanged rather than migrated.

Trial recipes: 2 gold ore and 1 wood make 1 refined gold in 24 work ticks;
2 refined gold make 1 gold ornament in 24 work ticks; 1 gold ornament and
1 diamond make 1 diamond ornament in 28 work ticks. These values remain
provisional for playtesting.
