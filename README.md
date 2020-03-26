## Recycle Cost

Current version 1.0.0 [Download](https://remod.org/RecycleCost.cs)

Uses Economics, ServerRewards.

### Overview

Recycle Cost allows an admin to charge for static recycler use.  This can be taken as X amount of an item such as wood, glue, bleach as 'fuel'.  The player will have to add this to the recycler input.

If using an item, the amount will be removed from the item stack similar to use with a furnace, etc.

You can use Economics or ServerRewards to take from the player's balance instead of requiring 'fuel'.

To flip the script with Economics or ServerRewards, you can also reward players with X amount of coins for each recycling cycle.

A small GUI will appear above the recycler loot table to show the requirements for recycler use.

### Configuration

```json
{
  "Settings": {
    "costItem": "wood",
    "costPerCycle": 1,
    "useEconomics": false,
    "useServerRewards": false,
    "recycleReward": false
  }
}
```

- `costItem` - Item to require for use of the recycler.  The player will need to add this to the recycler to turn it on.  Note that the short prefab name currently must end in .item, such as with wood, glue, bleach, etc.  This will be appended to this value, so do not add it here.
- `costPerCycle` - How much of the costItem to remove for each cycle.  Time to recycle and result of recycling will depend on Rust and any other plugins that may affect production.  This is an integer and should be positive, e.g. 1, 2, etc.
- `useEconomics` - Set true to use the Economics plugin for costPerCycle.
- `useServerRewards` - Set true to use the ServerRewards plugin for costPerCycle.
- `recycleReward` - Set true to PAY the player in costPerCycle for each recycling output/cycle.  This only works for Economics or ServerRewards and not with costItems such as wood.

### Future Plans

- We might work on allowing fractions of 1 item per cycle.  This may or may not be possible.
- Change the costItem configuration to use other non-items, if needed (prefabs that don't end in .item, if any).
