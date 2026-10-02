# Clinic supplies and illness care

Pending Windows game checks for [#565](https://github.com/compoodment/ClankerWorld/issues/565). The Clinic draft connects supplies, purchases and gradual illness care to normal play; it has not completed this hands-on playtest or merged into main.

On October 2, the owner chose to defer injury causes and injury treatment. Bandages can be made, stored and traded in this draft; using them on an injury waits for that later stage. Medicine supports gradual recovery from the game's existing illness.

- Watch a household build its Clinic with actual materials. Gather medicinal herbs from a reachable wild patch and carry them into the Clinic; stock at another House or a distant Clinic must not act as local ingredients.
- Make bandages from cloth at a House or Tailor Shop. Make medicine from herbs, wood fuel and fresh water delivered in a reusable jug. Check that the inputs decrease, the output appears at the actual workplace and the empty jug remains available for reuse.
- After the jug empties, watch an adult from the holding household pick it up at the Clinic and carry it back to the House. Repeat with an empty pot at a workstation. Neither vessel should move remotely, lose ownership or appear twice after reload. Another household's vessel, a reserved vessel or one without carrying/destination room must wait or remain untouched ([#749](https://github.com/compoodment/ClankerWorld/issues/749)).
- Remove the water or fuel needed for medicine. The job should explain what is missing and wait for physical delivery. Reload while the ingredients are reserved and confirm that the job does not spend them twice.
- Have an ill adult buy medicine from another household's Clinic. Both traders should meet for the exact exchange. The patient carries the purchase, payment goes to that Clinic's household and the buyer gains no access to its other stock or cooking.
- Let an ill adult use a dose personally. Recovery should take time, consume one actual dose and leave any remaining medicine in its original stock. Healthy agents should not consume an unnecessary dose.
- Let an adult choose a named caregiver, then have that caregiver bring medicine to the patient. Buying medicine, sharing a household or receiving an owner instruction must not create the patient's permission. A failed model reply, Jev choice or repeated intention must not grant it either.
- Revoke an adult caregiver's permission during treatment. Check that further care stops without recreating the dose already spent. A dependent's accepted caregiver may provide care; another relative or household adult must not gain that authority automatically.
- Interrupt treatment when the caregiver dies or a dependent's accepted care relationship ends. Keep the recovery already gained, stop the remaining effect and retain the spent dose without a refund. Save and reopen immediately afterward.
- Save and reopen during a medicine job, a Clinic purchase and a treatment course. Keep the same jug, remaining water, goods, payment, consent and progress. Pausing should freeze treatment time.
- Check that the agent and Clinic cards describe the actual supply or treatment state. An agent's model must not learn a distant patient's health or location merely because care is possible.

Trial construction and recipes: a 1×2 Clinic uses 10 wood and 4 stone; 1 cloth makes 2 bandages in 8 work ticks; 2 herbs, 1 fresh water and 1 wood make 2 medicine in 16 work ticks. These numbers remain provisional.

Keep a backup of worlds from older builds. This draft refuses older alpha saves and leaves them unchanged; it does not migrate them.
